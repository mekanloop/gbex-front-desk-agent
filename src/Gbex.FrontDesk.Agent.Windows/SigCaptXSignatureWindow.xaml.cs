using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Gbex.FrontDesk.Agent.Windows;

public partial class SigCaptXSignatureWindow : Window
{
    private static readonly string SigCaptXHtmlPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "SigCaptX",
        "gbex-stu-signature.html"
    );

    public string? CapturedFilePath { get; private set; }
    public string LastStatus { get; private set; } = "Wacom STU imza penceresi açıldı.";

    public SigCaptXSignatureWindow()
    {
        InitializeComponent();
        Loaded += SigCaptXSignatureWindow_Loaded;
    }

    private async void SigCaptXSignatureWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await SignatureWebView.EnsureCoreWebView2Async();
            SignatureWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            SignatureWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            if (!File.Exists(SigCaptXHtmlPath))
            {
                throw new FileNotFoundException("Wacom SigCaptX imza ekranı bulunamadı.", SigCaptXHtmlPath);
            }

            SignatureWebView.Source = new Uri(SigCaptXHtmlPath);
        }
        catch (Exception ex)
        {
            LastStatus = ex.Message;
            MessageBox.Show(this, ex.Message, "GBEX Wacom STU", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "status":
                    LastStatus = root.TryGetProperty("message", out var message)
                        ? message.GetString() ?? LastStatus
                        : LastStatus;
                    break;
                case "signature":
                    var dataUrl = root.GetProperty("dataUrl").GetString();
                    CapturedFilePath = SaveDataUrlToTempPng(dataUrl);
                    LastStatus = "Wacom STU imzası alındı.";
                    DialogResult = true;
                    break;
                case "cancel":
                    LastStatus = "Wacom STU imza alma iptal edildi.";
                    DialogResult = false;
                    break;
                case "error":
                    LastStatus = root.TryGetProperty("message", out var error)
                        ? error.GetString() ?? "Wacom STU imza hatası."
                        : "Wacom STU imza hatası.";
                    MessageBox.Show(this, LastStatus, "GBEX Wacom STU", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            LastStatus = ex.Message;
            MessageBox.Show(this, ex.Message, "GBEX Wacom STU", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SaveDataUrlToTempPng(string? dataUrl)
    {
        const string prefix = "data:image/png;base64,";
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Wacom imza görseli geçerli PNG formatında dönmedi.");
        }

        var bytes = Convert.FromBase64String(dataUrl[prefix.Length..]);
        var target = Path.Combine(
            Path.GetTempPath(),
            $"gbex-wacom-signature-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.png"
        );
        File.WriteAllBytes(target, bytes);
        return target;
    }
}
