using System.Globalization;
using System.Text.Json;
using FilesMate.App.Models;
using Markdig;

namespace FilesMate.App.Preview;

/// <summary>Opaque reader colors, independent of the floating window's material.</summary>
public sealed record MarkdownReaderTheme(bool Dark, bool HighContrast, string Background, string Foreground,
    string Muted, string CodeBackground, string Border, string Accent, string SelectionBackground, string SelectionForeground);

/// <summary>Shared, inert reading layout for the manager and the search popup.</summary>
public static class MarkdownPreview
{
    public const string ThemeStyleId = "filesmate-markdown-theme";
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras().UseAutoLinks().DisableHtml().Build();

    public static bool CanHandle(string path) => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown";

    // NavigateToString reports a generated data URI on some WebView2 runtimes.
    public static bool IsDocumentNavigation(string uri, bool userInitiated) =>
        uri.StartsWith("about:blank", StringComparison.Ordinal) ||
        !userInitiated && uri.StartsWith("data:text/html;charset=utf-8;base64,", StringComparison.OrdinalIgnoreCase);

    public static MarkdownReaderTheme CreateTheme(bool dark, string background, string foreground, string accent)
    {
        var palette = SkinPalette.For(dark);
        static string Rgb(uint color) => $"#{color & 0xFFFFFF:X6}";
        return NormalizeTheme(new(dark, false, background, foreground, Rgb(palette.Muted), Rgb(palette.Card),
            Rgb(palette.Outline), accent, accent, Rgb(AccentPalette.Foreground(ParseColor(accent)))));
    }

    // Resolve alpha once over the real opaque reader pixels, never over an
    // unrelated accent role or by dropping the alpha byte.
    public static string FlattenColor(uint argb, uint background, double opacity = 1)
    {
        var alpha = (argb >> 24) / 255d * Math.Clamp(double.IsFinite(opacity) ? opacity : 1, 0, 1);
        byte Channel(int shift) => (byte)Math.Clamp(Math.Round((byte)(argb >> shift) * alpha
            + (byte)(background >> shift) * (1 - alpha)), 0, 255);
        return $"#{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}";
    }

    public static MarkdownReaderTheme NormalizeTheme(MarkdownReaderTheme theme)
    {
        var background = Color(theme.Background);
        var foreground = ReadableInk(background, Color(theme.Foreground));
        var code = Color(theme.CodeBackground);
        var selection = Color(theme.SelectionBackground);
        return theme with
        {
            Background = background, Foreground = foreground,
            Muted = theme.HighContrast ? foreground : ReadableInk(background, Color(theme.Muted), foreground),
            CodeBackground = code, Border = Color(theme.Border), Accent = Color(theme.Accent),
            SelectionBackground = selection,
            SelectionForeground = ReadableInk(selection, Color(theme.SelectionForeground)),
        };
    }

    public static string Render(string text, bool dark, string background, string foreground, string accent) =>
        Render(text, CreateTheme(dark, background, foreground, accent));

    public static string Render(string text, MarkdownReaderTheme theme)
    {
        var body = Markdown.ToHtml(text, Pipeline);
        return $$"""
            <!doctype html><html><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src https://filesmate-preview.local; base-uri https://filesmate-preview.local; form-action 'none'">
            <base href="https://filesmate-preview.local/">
            <style id="{{ThemeStyleId}}">{{ThemeCss(theme)}}</style>
            <style>
            * { box-sizing: border-box; }
            body { margin:0; padding:8px 12px 24px 4px;
              font:14px/1.7 'Segoe UI','Microsoft YaHei UI',sans-serif; overflow-wrap:anywhere; }
            h1,h2,h3,h4,h5,h6 { color:var(--reader-ink);line-height:1.3; margin:1.2em 0 .55em; font-weight:600; }
            h1 {font-size:1.8em} h2 {font-size:1.5em} h3 {font-size:1.25em} body>:first-child {margin-top:0}
            p,ul,ol,pre,table,blockquote {margin:0 0 1em} ul,ol {padding-left:1.6em}
            li>p {margin-bottom:.35em} a {color:var(--reader-link);text-decoration:underline}
            pre,code {font-family:'Cascadia Mono',Consolas,monospace;font-size:.92em;color:var(--reader-code-ink)}
            code {background:var(--reader-code);padding:2px 5px;border-radius:4px}
            pre {padding:12px;border-radius:8px;background:var(--reader-code);overflow:auto;white-space:pre}
            pre code {padding:0;background:none} blockquote {margin-left:0;padding:4px 14px;border-left:3px solid var(--reader-line);color:var(--reader-muted)}
            table {border-collapse:collapse;display:block;overflow:auto} td,th {border:1px solid var(--reader-line);padding:6px 10px;text-align:left}
            th {background:var(--reader-code);color:var(--reader-code-ink)} img {max-width:100%;height:auto;border-radius:6px}
            hr {border:0;border-top:1px solid var(--reader-line);margin:20px 0}
            input[type=checkbox] {accent-color:var(--reader-link);pointer-events:none}
            ::selection {background:var(--reader-selection);color:var(--reader-selection-ink)}
            </style></head><body>{{body}}</body></html>
            """;
    }

    public static string ThemeCss(MarkdownReaderTheme theme)
    {
        theme = NormalizeTheme(theme);
        var link = ReadableInk(theme.Background, theme.Accent, theme.Foreground);
        var codeInk = ReadableInk(theme.CodeBackground, theme.Foreground);
        // HC colors are supplied by the platform; prevent Chromium from replacing
        // individual tokens with a second, unrelated forced-color palette.
        return $$"""
            :root { color-scheme: {{(theme.Dark ? "dark" : "light")}}; forced-color-adjust: {{(theme.HighContrast ? "none" : "auto")}};
              --reader-ink:{{theme.Foreground}};--reader-muted:{{theme.Muted}};--reader-code:{{theme.CodeBackground}};--reader-code-ink:{{codeInk}};
              --reader-line:{{theme.Border}};--reader-link:{{link}};--reader-selection:{{theme.SelectionBackground}};--reader-selection-ink:{{theme.SelectionForeground}}; }
            body {background:{{theme.Background}};color:var(--reader-ink)}
            """;
    }

    public static string ThemeUpdateScript(MarkdownReaderTheme theme, string? documentStamp = null)
    {
        if (documentStamp is not null) ReaderThemeDocument.ValidateStamp(documentStamp);
        var stampGuard = documentStamp is null ? "" : $"if (style.dataset.document !== {JsonSerializer.Serialize(documentStamp)}) return false;";
        return $$"""
            (() => { const style = document.getElementById('{{ThemeStyleId}}'); if (!style) return false;
              {{stampGuard}}
              const x = window.scrollX, y = window.scrollY;
              style.textContent = {{JsonSerializer.Serialize(ThemeCss(theme))}};
              window.scrollTo(x, y); return true; })()
            """;
    }

    private static string Color(string value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)
        ? value.ToUpperInvariant() : throw new ArgumentException("Expected an RGB color.");

    private static uint ParseColor(string value) => uint.Parse(Color(value).AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string ReadableInk(string background, string preferred, string? fallback = null)
    {
        static double Luminance(string value)
        {
            var rgb = ParseColor(value);
            static double Linear(byte channel) { var c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear((byte)(rgb >> 16)) + .7152 * Linear((byte)(rgb >> 8)) + .0722 * Linear((byte)rgb);
        }
        var bg = Luminance(background);
        var ink = Luminance(preferred);
        if ((Math.Max(bg, ink) + .05) / (Math.Min(bg, ink) + .05) >= 4.5) return preferred;
        if (fallback is not null) return ReadableInk(background, fallback);
        return $"#{AccentPalette.Foreground(ParseColor(background)) & 0xFFFFFF:X6}";
    }
}

public readonly record struct MarkdownThemeUpdate(long Document, MarkdownReaderTheme Theme);

/// <summary>Coalesces theme changes without retaining a document or its contents.</summary>
public sealed class MarkdownThemeUpdates
{
    private long _document;
    private bool _ready;
    private ulong? _navigation;
    private MarkdownReaderTheme? _desired, _applied;
    private MarkdownThemeUpdate? _active;
    public long Document => _document;

    public long Reset()
    {
        _document++; _ready = false; _desired = _applied = null; _active = null; _navigation = null;
        return _document;
    }

    // A reload uses the same native view but is a new document. Invalidate the
    // previous script completion and only accept this navigation's ready event.
    public void BeginNavigation(ulong navigation)
    {
        _document++; _ready = false; _applied = null; _active = null; _navigation = navigation;
    }
    public bool IsNavigation(ulong navigation) => _navigation == navigation;
    public void MarkNavigationReady(ulong navigation)
    {
        if (_navigation == navigation) _ready = true;
    }

    public void Request(MarkdownReaderTheme theme) => _desired = MarkdownPreview.NormalizeTheme(theme);
    public void MarkReady(long document) { if (document == _document) _ready = true; }
    public bool TryTake(out MarkdownThemeUpdate update)
    {
        update = default;
        if (!_ready || _active is not null || _desired is null || _desired == _applied) return false;
        update = new(_document, _desired); _active = update; return true;
    }

    public void Complete(MarkdownThemeUpdate update, bool applied = true)
    {
        if (_active != update || update.Document != _document) return;
        if (applied) _applied = update.Theme;
        else _ready = false;
        _active = null;
    }
}
