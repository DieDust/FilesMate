using System.Text.Json;

namespace FilesMate.App.Preview;

/// <summary>Only the owned PDF viewer chrome is themed; authored page pixels are untouched.</summary>
public static class PdfPreviewTheme
{
    public const string StyleId = "filesmate-pdf-theme";
    public const string StylePlaceholder = "<style id=\"filesmate-pdf-theme\"></style>";

    public static string ThemeCss(MarkdownReaderTheme theme)
    {
        theme = MarkdownPreview.NormalizeTheme(theme);
        var control = MarkdownPreview.NormalizeTheme(theme with { Background = theme.CodeBackground });
        var focus = MarkdownPreview.NormalizeTheme(theme with { Foreground = theme.Accent }).Foreground;
        return $$"""
            :root { color-scheme: {{(theme.Dark ? "dark" : "light")}}; forced-color-adjust: {{(theme.HighContrast ? "none" : "auto")}};
              --viewer-background:{{theme.Background}};--viewer-ink:{{theme.Foreground}};--viewer-muted:{{theme.Muted}};
              --viewer-control:{{control.Background}};--viewer-control-ink:{{control.Foreground}};--viewer-line:{{theme.Border}};
              --viewer-focus:{{focus}};--viewer-selection:{{theme.SelectionBackground}};--viewer-selection-ink:{{theme.SelectionForeground}}; }
            """;
    }

    // Registered once for this PDF-only WebView, before the first navigation.
    // Virtual-host mapped assets cannot be intercepted by WebResourceRequested.
    public static string InitializationScript(MarkdownReaderTheme theme) => $$"""
        (() => { if (window !== window.top || location.href.split('#')[0] !== '{{PdfPreviewAssets.ViewerUri}}') return;
          const apply = () => { const style = document.getElementById('{{StyleId}}'); if (!style) return;
            style.textContent = {{JsonSerializer.Serialize(ThemeCss(theme))}}; };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', apply, { once: true });
          else apply(); })()
        """;

    public static string ThemeUpdateScript(MarkdownReaderTheme theme, string documentStamp)
    {
        ReaderThemeDocument.ValidateStamp(documentStamp);
        return $$"""
            (() => { if (window !== window.top || location.href.split('#')[0] !== '{{PdfPreviewAssets.ViewerUri}}') return false;
              const style = document.getElementById('{{StyleId}}'); if (!style || style.dataset.document !== {{JsonSerializer.Serialize(documentStamp)}}) return false;
              style.textContent = {{JsonSerializer.Serialize(ThemeCss(theme))}}; return true; })()
            """;
    }
}
