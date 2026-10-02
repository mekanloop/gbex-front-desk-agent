using System.IO;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Gbex.FrontDesk.Agent.Windows.Services;

namespace Gbex.FrontDesk.Agent.Windows;

public partial class MainWindow : Window
{
    private static readonly Uri FrontDeskUri = new("https://panel.gbex.com.tr/admin/front-desk");
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GBEX",
        "FrontDeskAgent"
    );
    private static readonly string SecureScanFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GBEX",
        "FrontDesk",
        "Scans",
        "Incoming"
    );
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
    private readonly List<FileSystemWatcher> _identityWatchers = [];
    private ActiveCapture? _activeCapture;

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
        StartDefaultIdentityWatchFolders();
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
            FrontDeskWebView.CoreWebView2.WebMessageReceived += FrontDeskWebView_WebMessageReceived;
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

    private async void FrontDeskWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        WebCaptureCommand? command;
        try
        {
            command = JsonSerializer.Deserialize<WebCaptureCommand>(e.WebMessageAsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception ex)
        {
            LastActionText.Text = $"Web komutu okunamadı: {ex.Message}";
            return;
        }

        if (command?.Source != "gbex-front-desk-web" || string.IsNullOrWhiteSpace(command.CaptureSessionId) || string.IsNullOrWhiteSpace(command.AccountId))
        {
            return;
        }

        if (command.Action == "capture_identity")
        {
            await BeginIdentityCaptureAsync(command);
            return;
        }

        if (command.Action == "capture_signature")
        {
            await BeginSignatureCaptureAsync(command);
        }
    }

    private async Task BeginIdentityCaptureAsync(WebCaptureCommand command)
    {
        _activeCapture = new ActiveCapture("identity_scan", command.CaptureSessionId, command.AccountId, command.CustomerName ?? "Müşteri");
        NotifyWeb("identity_scan", "waiting", $"Kimlik tarama bekleniyor: {_activeCapture.CustomerName}");

        if (!_scanner.HasWiaScanner())
        {
            LastActionText.Text = $"WIA tarayıcı yok. Yazıcıdan GBEX hedefine tarayın: {SecureScanFolder}";
            WatchFolderStatusText.Text = $"Aktif satış için bekleniyor: {_activeCapture.CustomerName}. Tarama klasörü: {SecureScanFolder}";
            return;
        }

        await RunBusyAsync("Kimlik taranıyor...", async cancellationToken =>
        {
            var file = await _scanner.ScanIdentityDocumentAsync(cancellationToken);
            LastActionText.Text = $"Kimlik tarandı: {file}";
            await UploadIdentityFileAsync(file, _activeCapture, cancellationToken);
        });
    }

    private async Task BeginSignatureCaptureAsync(WebCaptureCommand command)
    {
        _activeCapture = new ActiveCapture("signature", command.CaptureSessionId, command.AccountId, command.CustomerName ?? "Müşteri");
        NotifyWeb("signature", "waiting", $"Wacom imzası bekleniyor: {_activeCapture.CustomerName}");
        LastActionText.Text = "Wacom STU/SigCaptX imza penceresi açılıyor.";

        try
        {
            var sigCaptXWindow = new SigCaptXSignatureWindow
            {
                Owner = this,
            };

            if (sigCaptXWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(sigCaptXWindow.CapturedFilePath))
            {
                await RunBusyAsync("Wacom STU imzası sisteme yükleniyor...", cancellationToken =>
                    UploadSignatureFileAsync(sigCaptXWindow.CapturedFilePath, _activeCapture, cancellationToken));
                return;
            }

            LastActionText.Text = $"{sigCaptXWindow.LastStatus} Uygulama içi imza ekranı açılıyor.";
        }
        catch (Exception ex)
        {
            LastActionText.Text = $"Wacom STU/SigCaptX açılamadı: {ex.Message}. Uygulama içi imza ekranı açılıyor.";
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
            UploadSignatureFileAsync(window.CapturedFilePath, _activeCapture, cancellationToken));
    }

    private async Task UploadIdentityFileAsync(string filePath, ActiveCapture? capture, CancellationToken cancellationToken)
    {
        if (capture is null || capture.Type != "identity_scan")
        {
            LastActionText.Text = "Aktif kimlik tarama komutu yok; dosya sisteme yüklenmedi.";
            return;
        }

        if (FrontDeskWebView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("Web panel henüz hazır değil.");
        }

        var uploader = new FrontDeskUploader(FrontDeskWebView.CoreWebView2, FrontDeskUri);
        using var result = await uploader.UploadIdentityDocumentAsync(filePath, capture.CaptureSessionId, capture.AccountId, cancellationToken);
        LastActionText.Text = $"Kimlik dosyası front desk paneline yüklendi: {Path.GetFileName(filePath)}";
        NotifyWeb("identity_scan", "uploaded", "Kimlik taraması aktif satışa yüklendi.", result.RootElement.Clone());
        _activeCapture = null;
    }

    private async Task UploadSignatureFileAsync(string filePath, ActiveCapture? capture, CancellationToken cancellationToken)
    {
        if (capture is null || capture.Type != "signature")
        {
            LastActionText.Text = "Aktif imza komutu yok; imza sisteme yüklenmedi.";
            return;
        }

        if (FrontDeskWebView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("Web panel henüz hazır değil.");
        }

        var uploader = new FrontDeskUploader(FrontDeskWebView.CoreWebView2, FrontDeskUri);
        using var result = await uploader.UploadSignatureImageAsync(filePath, capture.CaptureSessionId, capture.AccountId, cancellationToken);
        LastActionText.Text = $"İmza dosyası front desk paneline yüklendi: {Path.GetFileName(filePath)}";
        NotifyWeb("signature", "uploaded", "Müşteri imzası aktif satışa yüklendi.", result.RootElement.Clone());
        _activeCapture = null;
    }

    private void ProvisionDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        ProvisionSecureScanFolder(showMessage: true);
    }

    private void StartDefaultIdentityWatchFolders()
    {
        ProvisionSecureScanFolder(showMessage: false);
        StartWatchFolders([SecureScanFolder], persist: false, replaceExisting: true);
    }

    private void ProvisionSecureScanFolder(bool showMessage)
    {
        Directory.CreateDirectory(SecureScanFolder);
        TryShareSecureScanFolder();
        WatchFolderStatusText.Text = $"Hazır: {SecureScanFolder}. Paylaşım hedefi: GBEXSCAN$";
        LastActionText.Text = "Güvenli GBEX tarama klasörü hazırlandı. Yazıcı hedefi GBEXSCAN$ olarak ayarlanabilir.";
        if (showMessage)
        {
            MessageBox.Show(
                this,
                $"GBEX güvenli tarama klasörü hazırlandı.\n\nKlasör:\n{SecureScanFolder}\n\nPaylaşım adı:\nGBEXSCAN$\n\nNot: Yazıcı adres defteri otomatik yazılamıyorsa cihazda bu hedefin bir kez tanımlanması gerekir.",
                "GBEX Front Desk",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    private static void TryShareSecureScanFolder()
    {
        TryRunHiddenCommand($"/c net share GBEXSCAN$ /delete /y");
        if (!TryRunHiddenCommand($"/c net share GBEXSCAN$=\"{SecureScanFolder}\" /grant:Everyone,CHANGE"))
        {
            TryRunHiddenCommand($"/c net share GBEXSCAN$=\"{SecureScanFolder}\" /grant:Herkes,CHANGE");
        }

        TryRunHiddenCommand("/c netsh advfirewall firewall set rule group=\"File and Printer Sharing\" new enable=Yes");
        TryRunHiddenCommand("/c netsh advfirewall firewall set rule group=\"Dosya ve Yazıcı Paylaşımı\" new enable=Yes");
    }

    private static bool TryRunHiddenCommand(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(startInfo);
            if (process is null) return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void StartWatchFolders(IEnumerable<string> folders, bool persist, bool replaceExisting)
    {
        if (replaceExisting)
        {
            StopWatchFolders(null);
        }

        var selectedFolders = folders
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(folder => _identityWatchers.All(watcher => !string.Equals(watcher.Path, folder, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var folder in selectedFolders)
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            watcher.Created += IdentityWatcher_FileDetected;
            watcher.Renamed += IdentityWatcher_FileDetected;
            watcher.Changed += IdentityWatcher_FileDetected;
            _identityWatchers.Add(watcher);
        }


        if (_identityWatchers.Count > 0)
        {
            WatchFolderStatusText.Text = $"Açık: sadece güvenli GBEX klasörü izleniyor. Klasör: {SecureScanFolder}";
            LastActionText.Text = "Güvenli kimlik izleme açık. Aktif satış komutu yoksa dosya sisteme yüklenmez.";
        }
    }

    private void StopWatchFolders(string? status)
    {
        foreach (var watcher in _identityWatchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= IdentityWatcher_FileDetected;
            watcher.Renamed -= IdentityWatcher_FileDetected;
            watcher.Changed -= IdentityWatcher_FileDetected;
            watcher.Dispose();
        }
        _identityWatchers.Clear();

        if (!string.IsNullOrWhiteSpace(status))
        {
            WatchFolderStatusText.Text = "Kapalı.";
            LastActionText.Text = status;
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
            if (_activeCapture is null || _activeCapture.Type != "identity_scan")
            {
                LastActionText.Text = $"Tarama yakalandı ama aktif satış komutu yok; yüklenmedi: {Path.GetFileName(e.FullPath)}";
                return;
            }

            await RunBusyAsync($"Yeni kimlik dosyası yakalandı: {Path.GetFileName(e.FullPath)}", async cancellationToken =>
            {
                var readyPath = await WaitForFileReadyAsync(e.FullPath, cancellationToken);
                await UploadIdentityFileAsync(readyPath, _activeCapture, cancellationToken);
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
            NotifyWeb(_activeCapture?.Type ?? "agent", "error", ex.Message);
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
        RefreshDevicesButton.IsEnabled = !busy;
        ProvisionDeviceButton.IsEnabled = !busy;
        ReloadButton.IsEnabled = !busy;
        ConnectionStatusText.Text = status;
    }

    private void NotifyWeb(string type, string status, string message, JsonElement? payload = null)
    {
        if (FrontDeskWebView.CoreWebView2 is null)
        {
            return;
        }

        var detail = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["status"] = status,
            ["message"] = message,
        };
        if (payload.HasValue)
        {
            foreach (var property in payload.Value.EnumerateObject())
            {
                detail[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
            }
        }

        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var script = $"window.dispatchEvent(new CustomEvent('gbex-front-desk-agent', {{ detail: {json} }}));";
        _ = FrontDeskWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private sealed record WebCaptureCommand(
        string? Source,
        string? Action,
        string CaptureSessionId,
        string AccountId,
        string? CustomerName
    );

    private sealed record ActiveCapture(
        string Type,
        string CaptureSessionId,
        string AccountId,
        string CustomerName
    );
}
