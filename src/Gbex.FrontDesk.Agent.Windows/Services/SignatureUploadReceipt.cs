using System.Text.Json;

namespace Gbex.FrontDesk.Agent.Windows.Services;

internal static class SignatureUploadReceipt
{
    internal static void Validate(JsonElement upload, JsonElement readback, string sessionId, string accountId)
    {
        if (!upload.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
            || !upload.TryGetProperty("signature", out var signature)
            || !Matches(signature, sessionId, accountId)
            || !signature.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString())
            || !readback.TryGetProperty("signatures", out var signatures) || signatures.ValueKind != JsonValueKind.Array
            || !signatures.EnumerateArray().Any(item => Matches(item, sessionId, accountId)
                && item.TryGetProperty("id", out var readId) && readId.ValueKind == JsonValueKind.String
                && readId.GetString() == id.GetString()))
        {
            throw new InvalidOperationException("İmzanın aktif müşteriye kaydedildiği doğrulanamadı. Satışı tamamlamadan tekrar deneyin.");
        }
    }

    private static bool Matches(JsonElement item, string sessionId, string accountId) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("captureSessionId", out var session) && session.ValueKind == JsonValueKind.String
        && session.GetString() == sessionId
        && item.TryGetProperty("accountId", out var account) && account.ValueKind == JsonValueKind.String
        && account.GetString() == accountId;
}
