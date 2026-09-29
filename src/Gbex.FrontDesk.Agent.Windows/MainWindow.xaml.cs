using System.IO;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Gbex.FrontDesk.Agent.Windows.Services;

namespace Gbex.FrontDesk.Agent.Windows;

public partial class MainWindow : Window
{
    private static readonly Uri FrontDeskUri = new("https://app.gbex.com.tr/admin/front-desk");
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GBEX",
        "FrontDeskAgent"
    );
    private static readonly string WatchFolderConfigPath = Path.Combine(AppDataFolder, "identity-watch-folder.txt");
    private static readonly HashSet<string> WatchableIdentityExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".pdf",
    };

    private readonly DeviceMonitor _deviceMonitor = new();
    private readonly WiaScannerService _scanner = new();
    private readonly WacomSignatureService _wacom = new();
    private readonly HashSet<string> _processedWatchFiles = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _identityWatcher;

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
        TryResumeWatchFolder();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            ConnectionStatusText.Text = "WebView hazırlanıyor...";
            var userDataFolder = Path.Combine(AppDataFolder, "WebView2");
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
        if (!_wacom.IsSdkAvailable())
        {
            LastActionText.Text = "Wacom STU SDK hazır değil; uygulama içi imza ekranı açılıyor.";
        }
        else
        {
            LastActionText.Text = "Wacom STU SDK algılandı; bu sürümde güvenli imza yükleme için uygulama içi imza ekranı açılıyor.";
        }

        await CaptureSignatureWithAppCanvasAsync();
    }

    private async Task CaptureSignatureWithAppCanvasAsync()
    {
        var window = new SignatureCaptureWindow
        {
            Owner = this,
        };

        if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(window.CapturedFilePath))
        {
            LastActionText.Text = "İmza alma iptal edildi.";
            return;
        }

        await RunBusyAsync("İmza sisteme yükleniyor...", cancellationToken =>
            UploadSignatureFileAsync(window.CapturedFilePath, cancellationToken));
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

    private void WatchFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_identityWatcher is not null)
        {
            StopWatchFolder("Otomatik kimlik klasörü izleme kapatıldı.");
            TryDeleteWatchFolderConfig();
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Yumi tarayıcı yazılımının kaydettiği klasörden örnek bir dosya seçin",
            Filter = "Kimlik dosyaları|*.png;*.jpg;*.jpeg;*.pdf|Tüm dosyalar|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        var folder = Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        StartWatchFolder(folder, persist: true);
    }

    private void TryResumeWatchFolder()
    {
        try
        {
            if (!File.Exists(WatchFolderConfigPath))
            {
                return;
            }

            var folder = File.ReadAllText(WatchFolderConfigPath).Trim();
            if (Directory.Exists(folder))
            {
                StartWatchFolder(folder, persist: false);
            }
        }
        catch (Exception ex)
        {
            WatchFolderStatusText.Text = $"Önceki tarama klasörü açılamadı: {ex.Message}";
        }
    }

    private void StartWatchFolder(string folder, bool persist)
    {
        StopWatchFolder(null);

        _identityWatcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        _identityWatcher.Created += IdentityWatcher_FileDetected;
        _identityWatcher.Renamed += IdentityWatcher_FileDetected;

        if (persist)
        {
            Directory.CreateDirectory(AppDataFolder);
            File.WriteAllText(WatchFolderConfigPath, folder);
        }

        WatchFolderButton.Content = "Klasör İzlemeyi Durdur";
        WatchFolderStatusText.Text = $"Açık: {folder}";
        LastActionText.Text = "Tarama klasörü izleniyor. Bu klasöre düşen yeni kimlik dosyaları otomatik yüklenecek.";
    }

    private void StopWatchFolder(string? status)
    {
        if (_identityWatcher is not null)
        {
            _identityWatcher.EnableRaisingEvents = false;
            _identityWatcher.Created -= IdentityWatcher_FileDetected;
            _identityWatcher.Renamed -= IdentityWatcher_FileDetected;
            _identityWatcher.Dispose();
            _identityWatcher = null;
        }

        WatchFolderButton.Content = "Tarama Klasörü İzle";
        if (!string.IsNullOrWhiteSpace(status))
        {
            WatchFolderStatusText.Text = "Kapalı.";
            LastActionText.Text = status;
        }
    }

    private static void TryDeleteWatchFolderConfig()
    {
        try
        {
            if (File.Exists(WatchFolderConfigPath))
            {
                File.Delete(WatchFolderConfigPath);
            }
        }
        catch
        {
            // Best effort only; failing to delete the local preference must not block operators.
        }
    }

    private void IdentityWatcher_FileDetected(object sender, FileSystemEventArgs e)
    {
        if (!WatchableIdentityExtensions.Contains(Path.GetExtension(e.FullPath)))
        {
            return;
        }

        if (!_processedWatchFiles.Add(e.FullPath))
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(async () =>
        {
            await RunBusyAsync($"Yeni kimlik dosyası yakalandı: {Path.GetFileName(e.FullPath)}", async cancellationToken =>
            {
                var readyPath = await WaitForFileReadyAsync(e.FullPath, cancellationToken);
                await UploadIdentityFileAsync(readyPath, cancellationToken);
            });
        });
    }

    private static async Task<string> WaitForFileReadyAsync(string filePath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(filePath))
            {
                try
                {
                    await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > 0)
                    {
                        return filePath;
                    }
                }
                catch (IOException)
                {
                    // Scanner software is still writing the file.
                }
                catch (UnauthorizedAccessException)
                {
                    // Scanner software is still writing the file.
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new IOException($"Taranan dosya okunabilir hale gelmedi: {filePath}");
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
        WatchFolderButton.IsEnabled = !busy;
        UploadFileButton.IsEnabled = !busy;
        CaptureSignatureButton.IsEnabled = !busy;
        UploadSignatureButton.IsEnabled = !busy;
        RefreshDevicesButton.IsEnabled = !busy;
        ReloadButton.IsEnabled = !busy;
        ConnectionStatusText.Text = status;
    }
}
