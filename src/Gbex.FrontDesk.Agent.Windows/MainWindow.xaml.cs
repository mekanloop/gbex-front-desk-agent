using System.IO;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Gbex.FrontDesk.Agent.Windows.Services;

namespace Gbex.FrontDesk.Agent.Windows;

public partial class MainWindow : Window
{
    private static readonly Uri FrontDeskUri = new("https://app.gbex.com.tr/admin/front-desk");

    private readonly DeviceMonitor _deviceMonitor = new();
    private readonly WiaScannerService _scanner = new();
    private readonly WacomSignatureService _wacom = new();

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{App.AgentVersion}";
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeWebViewAsync();
        RefreshDeviceStatus();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            ConnectionStatusText.Text = "WebView hazırlanıyor...";
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GBEX",
                "FrontDeskAgent",
                "WebView2"
            );
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await FrontDeskWebView.EnsureCoreWebView2Async(env);
            FrontDeskWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            FrontDeskWebView.CoreWebView2.Settings.IsStatusBarEnabled = true;
            FrontDeskWebView.Source = FrontDeskUri;
            ConnectionStatusText.Text = "GBEX Front Desk paneli açıldı.";
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"Web panel açılamadı: {ex.Message}";
            LastActionText.Text = ex.ToString();
        }
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e) => RefreshDeviceStatus();

    private void RefreshDeviceStatus()
    {
        var devices = _deviceMonitor.DetectDevices();
        var scanners = devices.Where(d => d.Kind == "scanner").ToList();
        var signatures = devices.Where(d => d.Kind == "signature").ToList();

        ScannerStatusText.Text = scanners.Count > 0
            ? string.Join(Environment.NewLine, scanners.Select(d => $"✓ {d.Name} ({d.Status})"))
            : _scanner.IsWiaAvailable()
                ? "WIA hazır; tarayıcı bulunamadı. Sürücü/USB bağlantısını kontrol edin."
                : "WIA servisi bulunamadı. Scanner driver kurulumu gerekiyor.";

        WacomStatusText.Text = signatures.Count > 0
            ? string.Join(Environment.NewLine, signatures.Select(d => $"✓ {d.Name} ({d.Status})")) + Environment.NewLine + _wacom.StatusText(true)
            : _wacom.StatusText(false);

        LastActionText.Text = $"Cihaz taraması tamamlandı: {DateTime.Now:HH:mm:ss}";
    }

    private async void ScanIdentityButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Kimlik taranıyor...", async cancellationToken =>
        {
            var file = await _scanner.ScanIdentityDocumentAsync(cancellationToken);
            LastActionText.Text = $"Kimlik tarandı: {file}";
            await UploadIdentityFileAsync(file, cancellationToken);
        });
    }

    private async void UploadFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Kimlik dosyası seç",
            Filter = "Kimlik dosyaları|*.png;*.jpg;*.jpeg;*.pdf|Tüm dosyalar|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunBusyAsync("Kimlik dosyası yükleniyor...", cancellationToken =>
            UploadIdentityFileAsync(dialog.FileName, cancellationToken));
    }

    private async void CaptureSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Wacom imza cihazı hazırlanıyor...", async cancellationToken =>
        {
            var file = await _wacom.CaptureSignatureAsync(cancellationToken);
            LastActionText.Text = $"İmza alındı: {file}";
            await UploadSignatureFileAsync(file, cancellationToken);
        });
    }

    private async void UploadSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "İmza görseli seç",
            Filter = "İmza görselleri|*.png;*.jpg;*.jpeg|Tüm dosyalar|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunBusyAsync("İmza dosyası yükleniyor...", cancellationToken =>
            UploadSignatureFileAsync(dialog.FileName, cancellationToken));
    }

    private async Task UploadIdentityFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (FrontDeskWebView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("Web panel henüz hazır değil.");
        }

        var uploader = new FrontDeskUploader(FrontDeskWebView.CoreWebView2);
        await uploader.UploadIdentityDocumentAsync(filePath, cancellationToken);
        LastActionText.Text = $"Kimlik dosyası front desk paneline yüklendi: {Path.GetFileName(filePath)}";
    }

    private async Task UploadSignatureFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (FrontDeskWebView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("Web panel henüz hazır değil.");
        }

        var uploader = new FrontDeskUploader(FrontDeskWebView.CoreWebView2);
        await uploader.UploadSignatureImageAsync(filePath, cancellationToken);
        LastActionText.Text = $"İmza dosyası front desk paneline yüklendi: {Path.GetFileName(filePath)}";
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        FrontDeskWebView.CoreWebView2?.Reload();
        LastActionText.Text = "Panel yenilendi.";
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> operation)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        SetBusy(true, status);
        try
        {
            await operation(cts.Token);
            ConnectionStatusText.Text = "İşlem tamamlandı.";
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "İşlem başarısız.";
            LastActionText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "GBEX Front Desk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false, "Hazır.");
            RefreshDeviceStatus();
        }
    }

    private void SetBusy(bool busy, string status)
    {
        ScanIdentityButton.IsEnabled = !busy;
        UploadFileButton.IsEnabled = !busy;
        CaptureSignatureButton.IsEnabled = !busy;
        UploadSignatureButton.IsEnabled = !busy;
        RefreshDevicesButton.IsEnabled = !busy;
        ReloadButton.IsEnabled = !busy;
        ConnectionStatusText.Text = status;
    }
}
