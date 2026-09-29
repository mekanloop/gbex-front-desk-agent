using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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

    public async Task UploadIdentityDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        await UploadMultipartAsync(
            "/api/admin/front-desk/identity-scans",
            "identityDocument",
            filePath,
            "Kimlik yüklenemedi",
            cancellationToken
        );
    }

    public async Task UploadSignatureImageAsync(string filePath, CancellationToken cancellationToken)
    {
        await UploadMultipartAsync(
            "/api/admin/front-desk/signatures",
            "signatureImage",
            filePath,
            "İmza yüklenemedi",
            cancellationToken
        );
    }

    private async Task UploadMultipartAsync(
        string endpoint,
        string fieldName,
        string filePath,
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

        using var response = await client.PostAsync(endpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{errorPrefix} ({(int)response.StatusCode}): {body}");
        }
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
