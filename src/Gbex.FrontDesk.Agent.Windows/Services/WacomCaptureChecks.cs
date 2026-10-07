using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace Gbex.FrontDesk.Agent.Windows.Services;

internal static class WacomCaptureChecks
{
    // Synthetic pen reports exercise the production stroke processor and PNG
    // renderer. These checks do NOT simulate or claim a real USB capture.
    internal static int Run()
    {
        try
        {
            var ink = new SignatureInk(10000, 6000, 320, 200);
            Check(!ink.IsMeaningful, "empty capture rejected");
            for (var i = 0; i < 10; i++) ink.Add(100, 100, 100, true);
            ink.Add(9000, 4900, 0, false);
            Check(!ink.IsMeaningful, "hover cannot make a dot a valid signature");
            ink.Clear();
            ink.Add(1000, 1000, 120, true);
            ink.Add(1030, 1010, 120, true);
            ink.Add(1060, 1020, 120, true);
            ink.Add(1060, 1020, 0, false);
            Check(ink.IsMeaningful, "short but real pen stroke accepted");
            ink.Clear();
            Check(ink.Add(1000, 5500, 100, true) == 0, "button needs release");
            Check(ink.Add(1000, 5500, 0, false) == 1, "confirm button hit");
            Check(ink.Points.Count == 0, "button excluded from ink");
            ink.Add(1000, 5500, 100, true);
            Check(ink.Add(5000, 5500, 0, false) == 0, "drag between buttons not a click");
            ink.Add(5000, 5500, 100, true);
            Check(ink.Add(5000, 5500, 0, false) == 2, "clear button hit");
            ink.Add(9000, 5500, 100, true);
            Check(ink.Add(9000, 5500, 0, false) == 3, "cancel button hit");
            for (var run = 0; run < 10; run++)
            {
                ink.Clear();
                for (ushort i = 0; i < 10; i++)
                    ink.Add((ushort)(1000 + i * 100), (ushort)(1000 + i * 100), 200, true);
                ink.Add(1900, 1900, 0, false);
                for (ushort i = 0; i < 10; i++)
                    ink.Add((ushort)(7000 + i * 100), (ushort)(1000 + i * 100), 200, true);
                ink.Add(7900, 1900, 0, false);
                Check(ink.IsMeaningful, "valid strokes accepted");
                using var png = new MemoryStream();
                using (var bitmap = ink.Render()) bitmap.Save(png, ImageFormat.Png);
                png.Position = 0;
                using var decoded = new Bitmap(png);
                Check(decoded.Width == 1200 && decoded.Height == 720, "PNG aspect ratio");
                Check(decoded.GetPixel(180, 180).A > 0, "first stroke has visible ink");
                Check(decoded.GetPixel(900, 180).A > 0, "second stroke has visible ink");
                Check(decoded.GetPixel(540, 174).A == 0, "separate strokes not joined");
                Check(decoded.GetPixel(10, 10).A == 0, "background transparent");
            }
            ink.Clear();
            Check(!ink.IsMeaningful && ink.Points.Count == 0, "clear removes previous signature");
            using var upload = JsonDocument.Parse("""{"ok":true,"signature":{"id":"sig1","captureSessionId":"session1","accountId":"account1"}}""");
            using var readback = JsonDocument.Parse("""{"signatures":[{"id":"sig1","captureSessionId":"session1","accountId":"account1"}]}""");
            SignatureUploadReceipt.Validate(upload.RootElement, readback.RootElement, "session1", "account1");
            ExpectReceiptFailure(upload.RootElement, readback.RootElement, "session2", "account1");
            ExpectReceiptFailure(upload.RootElement, readback.RootElement, "session1", "account2");
            using var missing = JsonDocument.Parse("""{"signatures":[]}""");
            ExpectReceiptFailure(upload.RootElement, missing.RootElement, "session1", "account1");
            Console.WriteLine("WACOM_CAPTURE_CHECKS_OK: synthetic pen/button/PNG checks passed (not a hardware test).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WACOM_CAPTURE_CHECKS_FAILED: {ex}");
            return 1;
        }
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
    }

    private static void ExpectReceiptFailure(JsonElement upload, JsonElement readback, string session, string account)
    {
        try { SignatureUploadReceipt.Validate(upload, readback, session, account); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Unverified or mismatched signature receipt was accepted.");
    }
}
