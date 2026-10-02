using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Gbex.FrontDesk.Agent.Windows.Services;

public sealed class FrontDeskUploader
{
    private readonly CoreWebView2 _webView;
    private readonly Uri _baseUri;

    public FrontDeskUploader(CoreWebView2 webView, Uri baseUri)
    {
        _webView = webView;
        _baseUri = baseUri;
    }

    public async Task<JsonDocument> UploadIdentityDocumentAsync(
        string filePath,
        string captureSessionId,
        string accountId,
        CancellationToken cancellationToken)
    {
        return await UploadMultipartAsync(
            "/api/admin/front-desk/identity-scans",
            "identityDocument",
            filePath,
            captureSessionId,
            accountId,
            "Kimlik yüklenemedi",
            cancellationToken
        );
    }

    public async Task<JsonDocument> UploadSignatureImageAsync(
        string filePath,
        string captureSessionId,
        string accountId,
        CancellationToken cancellationToken)
    {
        return await UploadMultipartAsync(
            "/api/admin/front-desk/signatures",
            "signatureImage",
            filePath,
            captureSessionId,
            accountId,
            "İmza yüklenemedi",
            cancellationToken
        );
    }

    private async Task<JsonDocument> UploadMultipartAsync(
        string endpoint,
        string fieldName,
        string filePath,
        string captureSessionId,
        string accountId,
        string errorPrefix,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Yüklenecek dosya bulunamadı.", filePath);
        }

        var origin = _baseUri.GetLeftPart(UriPartial.Authority);
        var cookies = await _webView.CookieManager.GetCookiesAsync(origin);
        var cookieHeader = string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            throw new InvalidOperationException("GBEX oturum çerezi bulunamadı. Önce front desk paneline giriş yapın.");
        }

        using var client = new HttpClient { BaseAddress = new Uri(origin) };
        client.DefaultRequestHeaders.Add("Cookie", cookieHeader);

        await using var stream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(filePath));
        content.Add(fileContent, fieldName, Path.GetFileName(filePath));
        content.Add(new StringContent(captureSessionId), "captureSessionId");
        content.Add(new StringContent(accountId), "accountId");

        using var response = await client.PostAsync(endpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{errorPrefix} ({(int)response.StatusCode}): {body}");
        }

        return JsonDocument.Parse(body);
    }

    private static string ContentTypeFor(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
    }
}
