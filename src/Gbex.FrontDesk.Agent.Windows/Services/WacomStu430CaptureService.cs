using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace Gbex.FrontDesk.Agent.Windows.Services;

public sealed class WacomStu430CaptureService
{
    public string CaptureSignature()
    {
        wgssSTU.UsbDevices devices;
        try
        {
            devices = new wgssSTU.UsbDevices();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Wacom STU SDK/COM bileşeni yüklenemedi. GBEX Agent 32-bit Wacom modülüyle kurulmalı veya Wacom STU SDK PC'de kurulu olmalı.",
                ex
            );
        }

        wgssSTU.IUsbDevice? device = null;
        try
        {
            if (devices.Count == 0)
                throw new InvalidOperationException("Wacom STU-430 cihazı bulunamadı. USB bağlantısını kontrol edin.");

            device = devices[0];
            using var form = new WacomStu430SignatureForm(device);
            var result = form.ShowDialog();
            if (result == WinFormsDialogResult.Cancel)
                throw new OperationCanceledException("Wacom imza alma iptal edildi.");

            if (result != WinFormsDialogResult.OK || string.IsNullOrWhiteSpace(form.SignatureFilePath))
                throw new InvalidOperationException(form.LastError ?? "Wacom STU imzası alınamadı.");

            return form.SignatureFilePath;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(devices);
        }
    }
}

internal enum WacomPenDataMode
{
    None = 0,
    TimeCount = 1,
    SequenceNumber = 2,
    TimeCountSequence = 3,
}

internal sealed class WacomStu430SignatureForm : Form
{
    private delegate void PadButtonClick();

    private struct PadButton
    {
        public Rectangle Bounds;
        public string Text;
        public PadButtonClick Click;
    }

    private readonly wgssSTU.IUsbDevice _usbDevice;
    private wgssSTU.Tablet? _tablet;
    private wgssSTU.ICapability? _capability;
    private wgssSTU.IInformation? _information;
    private SignatureInk? _ink;
    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = 120000 };
    private PadButton[] _buttons = [];
    private Bitmap? _padBitmap;
    private byte[]? _padBitmapData;
    private wgssSTU.encodingMode _encodingMode;
    private bool _completed;
    private bool _closing;

    public string? SignatureFilePath { get; private set; }
    public string? LastError { get; private set; }

    public WacomStu430SignatureForm(wgssSTU.IUsbDevice usbDevice)
    {
        _usbDevice = usbDevice;
        Text = "GBEX Wacom STU";
        Width = 460;
        Height = 220;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Text = "Müşteri Wacom STU-430 cihazında imza atıyor.\n\nİmza cihaz ekranında atılacak.\nBittiğinde cihazdaki OK tuşuna basın.",
        });
        Shown += async (_, _) => await BeginCaptureAsync();
        FormClosed += (_, _) => DisconnectTablet();
        _timeout.Tick += (_, _) => FailCapture("İmza süresi doldu. Yeniden imza almayı başlatın.");
    }

    private async Task BeginCaptureAsync()
    {
        try
        {
            _tablet = new wgssSTU.Tablet();
            // Wacom's DemoButtons notes that STU Display/background processes
            // may hold the device briefly; retry without blocking the UI.
            for (var attempt = 0; ; attempt++)
            {
                if (_closing) return;
                var error = _tablet.usbConnect(_usbDevice, true);
                var value = error.value;
                var message = error.message;
                Marshal.ReleaseComObject(error);
                if (value == 0) break;
                if (attempt == 3)
                    throw new InvalidOperationException($"Wacom USB bağlantısı kurulamadı. Cihazı kullanan diğer imza uygulamalarını kapatın. ({value}: {message})");
                await Task.Delay(350);
            }

            _capability = _tablet.getCapability();
            _information = _tablet.getInformation();
            _ink = new SignatureInk(_capability.tabletMaxX, _capability.tabletMaxY,
                _capability.screenWidth, _capability.screenHeight);
            ConfigurePenDataMode();
            ConfigureButtons();
            ConfigurePadImage();
            AddTabletDelegates();
            ClearPad();
            _tablet.setInkingMode(0x01);
            _timeout.Start();
        }
        catch (Exception ex)
        {
            if (_closing) return;
            LastError = $"Wacom STU bağlantısı kurulamadı: {ex.Message}";
            DialogResult = WinFormsDialogResult.Abort;
            Close();
        }
    }

    private void ConfigurePenDataMode()
    {
        if (_tablet is null) return;

        try
        {
            // The Wacom C# DemoButtons reference capture uses the base
            // onPenData stream. STU-430 option reports vary by firmware, so
            // force the portable mode rather than selecting a report stream
            // that a particular tablet may not dispatch to our event sink.
            _tablet.setPenDataOptionMode((byte)wgssSTU.penDataOptionMode.PenDataOptionMode_None);
        }
        catch
        {
            // Some older firmware does not expose this setting. Its default
            // report stream is still handled by onPenData below.
        }
    }

    private void ConfigureButtons()
    {
        if (_capability is null) return;

        var w2 = _capability.screenWidth / 3;
        var w3 = _capability.screenWidth / 3;
        var w1 = _capability.screenWidth - w2 - w3;
        var y = _capability.screenHeight * 6 / 7;
        var h = _capability.screenHeight - y;

        _buttons =
        [
            new PadButton { Bounds = new Rectangle(0, y, w1, h), Text = "OK", Click = ConfirmSignature },
            new PadButton { Bounds = new Rectangle(w1, y, w2, h), Text = "TEMIZLE", Click = ClearPad },
            new PadButton { Bounds = new Rectangle(w1 + w2, y, w3, h), Text = "IPTAL", Click = CancelSignature },
        ];
    }

    private void ConfigurePadImage()
    {
        if (_tablet is null || _capability is null) return;

        var helper = new wgssSTU.ProtocolHelper();
        var encodingFlag = (wgssSTU.encodingFlag)helper.simulateEncodingFlag(_tablet.getProductId(), 0);
        if ((encodingFlag & wgssSTU.encodingFlag.EncodingFlag_24bit) != 0)
        {
            _encodingMode = _tablet.supportsWrite()
                ? wgssSTU.encodingMode.EncodingMode_24bit_Bulk
                : wgssSTU.encodingMode.EncodingMode_24bit;
        }
        else if ((encodingFlag & wgssSTU.encodingFlag.EncodingFlag_16bit) != 0)
        {
            _encodingMode = _tablet.supportsWrite()
                ? wgssSTU.encodingMode.EncodingMode_16bit_Bulk
                : wgssSTU.encodingMode.EncodingMode_16bit;
        }
        else
        {
            _encodingMode = wgssSTU.encodingMode.EncodingMode_1bit;
        }

        _padBitmap = new Bitmap(_capability.screenWidth, _capability.screenHeight, PixelFormat.Format32bppArgb);
        using (var gfx = Graphics.FromImage(_padBitmap))
        using (var buttonFont = new Font(FontFamily.GenericSansSerif, Math.Max(12, _buttons[0].Bounds.Height / 2F), GraphicsUnit.Pixel))
        using (var titleFont = new Font(FontFamily.GenericSansSerif, 16F, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var smallFont = new Font(FontFamily.GenericSansSerif, 10F, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            gfx.Clear(Color.White);
            gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel;
            gfx.DrawString("LUTFEN IMZALAYIN", titleFont, Brushes.Black, new PointF(10, 10));
            gfx.DrawString("Bittiginde OK tusuna basin", smallFont, Brushes.Black, new PointF(10, 30));
            var lineY = (int)(_capability.screenHeight * 0.65);
            gfx.DrawLine(Pens.Black, 14, lineY, _capability.screenWidth - 14, lineY);
            foreach (var button in _buttons)
            {
                gfx.DrawRectangle(Pens.Black, button.Bounds);
                gfx.DrawString(button.Text, buttonFont, Brushes.Black, button.Bounds, sf);
            }
        }

        using var stream = new MemoryStream();
        _padBitmap.Save(stream, ImageFormat.Png);
        _padBitmapData = (byte[])helper.resizeAndFlatten(
            stream.ToArray(),
            0,
            0,
            (uint)_padBitmap.Width,
            (uint)_padBitmap.Height,
            _capability.screenWidth,
            _capability.screenHeight,
            (byte)_encodingMode,
            wgssSTU.Scale.Scale_Fit,
            0,
            0
        );

        Marshal.ReleaseComObject(helper);
    }

    private void AddTabletDelegates()
    {
        if (_tablet is null) return;
        _tablet.onGetReportException += OnGetReportException;
        _tablet.onPenData += OnPenData;
        _tablet.onPenDataEncrypted += OnPenDataEncrypted;
        _tablet.onPenDataTimeCountSequence += OnPenDataTimeCountSequence;
        _tablet.onPenDataTimeCountSequenceEncrypted += OnPenDataTimeCountSequenceEncrypted;
    }

    private void RemoveTabletDelegates()
    {
        if (_tablet is null) return;
        try
        {
            _tablet.onGetReportException -= OnGetReportException;
            _tablet.onPenData -= OnPenData;
            _tablet.onPenDataEncrypted -= OnPenDataEncrypted;
            _tablet.onPenDataTimeCountSequence -= OnPenDataTimeCountSequence;
            _tablet.onPenDataTimeCountSequenceEncrypted -= OnPenDataTimeCountSequenceEncrypted;
        }
        catch
        {
            // Best-effort cleanup for COM event sinks.
        }
    }

    private void ClearPad()
    {
        if (_tablet is null || _padBitmapData is null) return;
        _ink?.Clear();
        _tablet.writeImage((byte)_encodingMode, _padBitmapData);
        Invalidate();
    }

    private void ConfirmSignature()
    {
        if (_ink is null || !_ink.IsMeaningful)
        {
            LastError = "İmza boş veya çok kısa. Lütfen tekrar imza alın.";
            MessageBox.Show(this, LastError, "GBEX Wacom STU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ClearPad();
            return;
        }

        SignatureFilePath = RenderSignaturePng();
        _completed = true;
        DialogResult = WinFormsDialogResult.OK;
        Close();
    }

    private void CancelSignature()
    {
        DialogResult = WinFormsDialogResult.Cancel;
        Close();
    }

    private string RenderSignaturePng()
    {
        if (_ink is null) throw new InvalidOperationException("Wacom ekran bilgisi okunamadı.");
        using var bitmap = _ink.Render();

        var target = Path.Combine(
            Path.GetTempPath(),
            $"gbex-wacom-stu430-signature-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.png"
        );
        bitmap.Save(target, ImageFormat.Png);
        return target;
    }

    private void OnGetReportException(wgssSTU.ITabletEventsException exception)
    {
        try
        {
            exception.getException();
        }
        catch (Exception ex)
        {
            DispatchPadAction(() => FailCapture($"Wacom bağlantısı koptu: {ex.Message}"));
        }
    }

    private void OnPenDataEncrypted(wgssSTU.IPenDataEncrypted penData)
    {
        OnPenData(penData.penData1);
        OnPenData(penData.penData2);
    }

    private void OnPenDataTimeCountSequenceEncrypted(wgssSTU.IPenDataTimeCountSequenceEncrypted penData)
    {
        OnPenDataTimeCountSequence(penData);
    }

    private void OnPenDataTimeCountSequence(wgssSTU.IPenDataTimeCountSequence penData)
    {
        QueuePoint(penData.x, penData.y, penData.pressure, penData.sw != 0);
    }

    private void OnPenData(wgssSTU.IPenData penData)
    {
        QueuePoint(penData.x, penData.y, penData.pressure, penData.sw != 0);
    }

    private void QueuePoint(ushort x, ushort y, ushort pressure, bool down) =>
        DispatchPadAction(() => HandlePoint(x, y, pressure, down));

    private void DispatchPadAction(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_closing || IsDisposed) return;
                try { action(); }
                catch (Exception ex) { FailCapture($"Wacom imzası işlenemedi: {ex.Message}"); }
            }));
        }
        catch (InvalidOperationException) when (_closing || IsDisposed || !IsHandleCreated) { }
    }

    private void FailCapture(string message)
    {
        if (_closing) return;
        LastError = message;
        DialogResult = WinFormsDialogResult.Abort;
        Close();
    }

    private void HandlePoint(ushort x, ushort y, ushort pressure, bool down)
    {
        var button = _ink?.Add(x, y, pressure, down) ?? 0;
        // Already on the UI thread. Handle the action before the next pen
        // report so clearing/confirming cannot race queued stroke data.
        if (button > 0) _buttons[button - 1].Click();
    }

    private void DisconnectTablet()
    {
        _closing = true;
        _timeout.Stop();
        _timeout.Dispose();
        RemoveTabletDelegates();
        if (_tablet is not null)
        {
            try { _tablet.setInkingMode(0x00); } catch { }
            try { _tablet.setClearScreen(); } catch { }
            try { _tablet.disconnect(); } catch { }
            if (_information is not null) Marshal.ReleaseComObject(_information);
            if (_capability is not null) Marshal.ReleaseComObject(_capability);
            Marshal.ReleaseComObject(_tablet);
            _information = null;
            _capability = null;
            _tablet = null;
        }

        _padBitmap?.Dispose();
        if (!_completed && DialogResult == WinFormsDialogResult.None)
        {
            DialogResult = WinFormsDialogResult.Cancel;
        }
    }

}
