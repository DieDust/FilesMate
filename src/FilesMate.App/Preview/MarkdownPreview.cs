using Markdig;

namespace FilesMate.App.Preview;

/// <summary>Shared, inert reading layout for the manager and the search popup.</summary>
public static class MarkdownPreview
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras().UseAutoLinks().DisableHtml().Build();

    public static bool CanHandle(string path) => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown";

    // NavigateToString reports a generated data URI on some WebView2 runtimes.
    public static bool IsDocumentNavigation(string uri, bool userInitiated) =>
        uri.StartsWith("about:blank", StringComparison.Ordinal) ||
        !userInitiated && uri.StartsWith("data:text/html;charset=utf-8;base64,", StringComparison.OrdinalIgnoreCase);

    public static string Render(string text, bool dark, string background, string foreground, string accent)
    {
        static string Color(string value) => value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)
            ? value : throw new ArgumentException("Expected an RGB color.");
        var body = Markdown.ToHtml(text, Pipeline);
        return $$"""
            <!doctype html><html><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src https://filesmate-preview.local; base-uri https://filesmate-preview.local; form-action 'none'">
            <base href="https://filesmate-preview.local/">
            <style>
            :root { color-scheme: {{(dark ? "dark" : "light")}}; }
            * { box-sizing: border-box; }
            body { margin:0; padding:8px 12px 24px 4px; background:{{Color(background)}}; color:{{Color(foreground)}};
              font:14px/1.7 'Segoe UI','Microsoft YaHei UI',sans-serif; overflow-wrap:anywhere; }
            h1,h2,h3,h4,h5,h6 { line-height:1.3; margin:1.2em 0 .55em; font-weight:600; }
            h1 {font-size:1.8em} h2 {font-size:1.5em} h3 {font-size:1.25em} body>:first-child {margin-top:0}
            p,ul,ol,pre,table,blockquote {margin:0 0 1em} ul,ol {padding-left:1.6em}
            li>p {margin-bottom:.35em} a {color:{{Color(accent)}};text-decoration:none} a:hover {text-decoration:underline}
            pre,code {font-family:'Cascadia Mono',Consolas,monospace;font-size:.92em}
            code {background:{{(dark ? "#353535" : "#eeeeee")}};padding:2px 5px;border-radius:4px}
            pre {padding:12px;border-radius:8px;background:{{(dark ? "#353535" : "#eeeeee")}};overflow:auto;white-space:pre}
            pre code {padding:0;background:none} blockquote {margin-left:0;padding:4px 14px;border-left:3px solid {{Color(accent)}};opacity:.85}
            table {border-collapse:collapse;display:block;overflow:auto} td,th {border:1px solid {{(dark ? "#555555" : "#cccccc")}};padding:6px 10px;text-align:left}
            th {background:{{(dark ? "#353535" : "#eeeeee")}}} img {max-width:100%;height:auto;border-radius:6px}
            hr {border:0;border-top:1px solid {{(dark ? "#555555" : "#cccccc")}};margin:20px 0}
            input[type=checkbox] {accent-color:{{Color(accent)}};pointer-events:none}
            ::selection {background:{{Color(accent)}};color:white}
            </style></head><body>{{body}}</body></html>
            """;
    }
}
