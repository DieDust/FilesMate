using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FilesMate.Search;

public static class AppearancePreviewProtocol
{
    public const string Acquire = "acquire";
    public const string Update = "update";
    public const string Clear = "clear";
    public static bool IsOperation(string? operation) => operation is Acquire or Update or Clear;

    public const int MaximumSkinCharacters = 3000;
    public static string Fingerprint(SearchAppearancePreview preview) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preview))));

    public static bool TryRead(string message, out SearchHostRequest? request)
    {
        request = null;
        if (message is null || message.Length > SearchHostProtocol.MaximumRequestCharacters) return false;
        try
        {
            using var document = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (!Object(root, ["Command", "Settings", "ManagerPath", "AppearancePreview", "PreviewOwnerPid",
                "PreviewSessionId", "PreviewEpoch", "PreviewOwnerStartedUtcTicks", "PreviewOperation"])) return false;
            if (!root.TryGetProperty("Command", out var command) || command.GetString() != "appearance-preview"
                || root.TryGetProperty("Settings", out var settings) && settings.ValueKind != JsonValueKind.Null
                || root.TryGetProperty("ManagerPath", out var path) && path.ValueKind != JsonValueKind.Null
                || !root.TryGetProperty("AppearancePreview", out var preview)
                || !root.TryGetProperty("PreviewOperation", out var operation) || operation.ValueKind != JsonValueKind.String
                || !IsOperation(operation.GetString())
                || (operation.GetString() == Update) != (preview.ValueKind != JsonValueKind.Null)
                || !root.TryGetProperty("PreviewOwnerPid", out var pid) || !pid.TryGetInt32(out var owner) || owner <= 0
                || !root.TryGetProperty("PreviewOwnerStartedUtcTicks", out var started) || !started.TryGetInt64(out var ticks)
                || ticks <= 0 || ticks > DateTime.MaxValue.Ticks
                || !root.TryGetProperty("PreviewEpoch", out var version) || !version.TryGetInt64(out var epoch) || epoch <= 0
                || !root.TryGetProperty("PreviewSessionId", out var id) || id.ValueKind != JsonValueKind.String
                || id.GetString() is not { Length: 32 } sessionId || !Guid.TryParseExact(sessionId, "N", out var session)
                || session == Guid.Empty) return false;
            if (preview.ValueKind != JsonValueKind.Null)
            {
                string[] fields = ["Theme", "Backdrop", "ReduceMotion", "GlassEffect", "Accent", "CustomAccent", "TransparencyPercent", "SkinJson"];
                if (!Object(preview, fields) || fields.Any(field => !preview.TryGetProperty(field, out _))) return false;
                foreach (var field in fields[..5])
                    if (preview.GetProperty(field).ValueKind != JsonValueKind.String
                        || preview.GetProperty(field).GetString() is not { Length: > 0 and <= 24 }) return false;
                if (!preview.GetProperty("TransparencyPercent").TryGetInt32(out var opacity) || opacity is < 0 or > 100
                    || !OptionalString(preview.GetProperty("CustomAccent"), 9)
                    || !OptionalString(preview.GetProperty("SkinJson"), MaximumSkinCharacters)) return false;
            }
            request = JsonSerializer.Deserialize<SearchHostRequest>(message);
            return request is not null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return false; }
    }

    private static bool OptionalString(JsonElement value, int maximum) => value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= maximum;

    private static bool Object(JsonElement value, string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        HashSet<string> names = new(StringComparer.Ordinal);
        return value.EnumerateObject().All(property => names.Add(property.Name) && allowed.Contains(property.Name, StringComparer.Ordinal));
    }
}
