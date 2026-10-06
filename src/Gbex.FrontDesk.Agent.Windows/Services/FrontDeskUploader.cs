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
            throw new InvalidOperationException($"{errorPrefix} ({(int)response.StatusCode}): {ExtractErrorMessage(body)}");
        }

        var result = JsonDocument.Parse(body);
        if (fieldName == "signatureImage")
        {
            try
            {
                // Read the persisted record through the same staff/session scope.
                // A 2xx upload alone is not enough to notify the panel of success.
                var readbackUrl = $"{endpoint}?captureSessionId={Uri.EscapeDataString(captureSessionId)}&accountId={Uri.EscapeDataString(accountId)}";
                using var readbackResponse = await client.GetAsync(readbackUrl, cancellationToken);
                if (!readbackResponse.IsSuccessStatusCode)
                    throw new InvalidOperationException("İmza gönderildi ancak sunucudaki kayıt doğrulanamadı. Satışı tamamlamadan tekrar deneyin.");
                using var readback = JsonDocument.Parse(await readbackResponse.Content.ReadAsStringAsync(cancellationToken));
                SignatureUploadReceipt.Validate(result.RootElement, readback.RootElement, captureSessionId, accountId);
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        return result;
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Sunucu boş hata cevabı döndürdü.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var errorMessage)
                && errorMessage.ValueKind == JsonValueKind.String)
            {
                return errorMessage.GetString() ?? body;
            }
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Keep the original server response below.
        }

        return body.Length > 600 ? body[..600] + "..." : body;
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
