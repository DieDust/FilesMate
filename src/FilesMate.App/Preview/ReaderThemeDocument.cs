using System.Text.Json;

namespace FilesMate.App.Preview;

/// <summary>Document-local identity shared by the two owned HTML readers.</summary>
public static class ReaderThemeDocument
{
    public static string CaptureStampScript(bool pdf, string proposedStamp)
    {
        ValidateStamp(proposedStamp);
        var styleId = pdf ? PdfPreviewTheme.StyleId : MarkdownPreview.ThemeStyleId;
        var originGuard = pdf ? $"if (location.href.split('#')[0] !== '{PdfPreviewAssets.ViewerUri}') return '';" : "";
        // Never replace an existing stamp. A stale capture can seed a newly loaded
        // page, but the current capture will read that same stamp; the host also
        // checks its navigation id after await before accepting the result.
        return $$"""
            (() => { if (window !== window.top) return ''; {{originGuard}}
              const style = document.getElementById('{{styleId}}'); if (!style) return '';
              return style.dataset.document || (style.dataset.document = {{JsonSerializer.Serialize(proposedStamp)}}); })()
            """;
    }

    public static bool TryReadStamp(string json, out string stamp)
    {
        stamp = "";
        if (json.Length > 64) return false;
        try
        {
            var value = JsonSerializer.Deserialize<string>(json);
            if (!Guid.TryParseExact(value, "D", out var parsed)) return false;
            stamp = parsed.ToString("D"); return true;
        }
        catch (JsonException) { return false; }
    }

    internal static void ValidateStamp(string stamp)
    {
        if (!Guid.TryParseExact(stamp, "D", out _)) throw new ArgumentException("Expected a document stamp.", nameof(stamp));
    }
}
