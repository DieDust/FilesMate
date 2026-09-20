using System.Globalization;
using System.Text;

namespace FilesMate.BrandAssets;

internal static class FileIconAssets
{
    private enum Decoration
    {
        Lines,
        Grid,
        Slide,
        Code,
        Book,
        Database,
        Settings,
        System,
        Generic,
    }

    private sealed record DocumentSpec(string FileName, string Label, string Accent, Decoration Decoration);

    private sealed record ArchiveSpec(string FileName, string Label, string Accent);

    private static readonly DocumentSpec[] Documents =
    [
        new("document.svg", "DOC", "#64748B", Decoration.Lines),
        new("pdf.svg", "PDF", "#E5484D", Decoration.Lines),
        new("word.svg", "DOC", "#3B82F6", Decoration.Lines),
        new("spreadsheet.svg", "XLS", "#229A68", Decoration.Grid),
        new("presentation.svg", "PPT", "#F06F2F", Decoration.Slide),
        new("text.svg", "TXT", "#64748B", Decoration.Lines),
        new("markdown.svg", "MD", "#7C5CFC", Decoration.Lines),
        new("json.svg", "JSON", "#D17A12", Decoration.Code),
        new("xml.svg", "XML", "#0F9388", Decoration.Code),
        new("html.svg", "HTML", "#D85A35", Decoration.Code),
        new("csv.svg", "CSV", "#2E9562", Decoration.Grid),
        new("log.svg", "LOG", "#475569", Decoration.Lines),
        new("ebook.svg", "EPUB", "#B94B86", Decoration.Book),
        new("code.svg", "CODE", "#5865C7", Decoration.Code),
        new("javascript.svg", "JS", "#B98500", Decoration.Code),
        new("typescript.svg", "TS", "#3178C6", Decoration.Code),
        new("csharp.svg", "C#", "#74439B", Decoration.Code),
        new("cpp.svg", "CPP", "#2769B2", Decoration.Code),
        new("python.svg", "PY", "#A77718", Decoration.Code),
        new("rust.svg", "RS", "#4B5563", Decoration.Code),
        new("go.svg", "GO", "#0D91A6", Decoration.Code),
        new("java.svg", "JAVA", "#C95B38", Decoration.Code),
        new("yaml.svg", "YML", "#6D5BD0", Decoration.Settings),
        new("toml.svg", "TOML", "#A45B32", Decoration.Settings),
        new("database.svg", "DB", "#0B9189", Decoration.Database),
        new("configuration.svg", "CFG", "#7359C7", Decoration.Settings),
        new("system.svg", "SYS", "#596473", Decoration.System),
        new("generic.svg", "FILE", "#64748B", Decoration.Generic),
    ];

    private static readonly ArchiveSpec[] Archives =
    [
        new("archive.svg", "PACK", "#B96E13"),
        new("zip.svg", "ZIP", "#C77712"),
        new("sevenzip.svg", "7Z", "#444B59"),
        new("rar.svg", "RAR", "#79519B"),
        new("tar.svg", "TAR", "#986035"),
    ];

    private static readonly IReadOnlyDictionary<char, string> StrokeGlyphs = new Dictionary<char, string>
    {
        ['#'] = "M1.4 5 H6.6 M1.4 9 H6.6 M3 2.2 V12.2 M5 2.2 V12.2",
        ['7'] = "M1.2 2.2 H6.8 L3.2 12.4",
        ['A'] = "M1 12.4 L4 1.8 L7 12.4 M2.3 8.4 H5.7",
        ['B'] = "M1.4 1.8 V12.4 M1.4 1.8 H5.1 C6.9 1.8 6.9 6.9 5.1 6.9 H1.4 M5.1 6.9 C7.1 6.9 7.1 12.4 5.1 12.4 H1.4",
        ['C'] = "M6.6 3.4 C5.7 2 4.1 1.6 2.8 2.4 C1.2 3.4 1 10.8 2.8 11.8 C4.1 12.6 5.7 12.2 6.6 10.8",
        ['D'] = "M1.4 1.8 V12.4 H4.4 C6.8 12.4 7.4 9.6 7.4 7.1 C7.4 4.6 6.8 1.8 4.4 1.8 Z",
        ['E'] = "M6.5 1.8 H1.5 V12.4 H6.5 M1.5 7.1 H5.4",
        ['F'] = "M6.5 1.8 H1.5 V12.4 M1.5 7.1 H5.3",
        ['G'] = "M6.5 3.6 C5.6 2.1 4 1.6 2.8 2.5 C1.2 3.6 1.1 10.7 2.8 11.7 C4.1 12.6 5.8 12.2 6.6 10.6 V7.6 H4.4",
        ['H'] = "M1.5 1.8 V12.4 M6.5 1.8 V12.4 M1.5 7.1 H6.5",
        ['I'] = "M2.2 1.8 H5.8 M4 1.8 V12.4 M2.2 12.4 H5.8",
        ['J'] = "M2.4 1.8 H6.6 V9.6 C6.6 12.2 3.4 12.6 2.2 10.6",
        ['K'] = "M1.5 1.8 V12.4 M6.5 1.8 L1.8 7.4 L6.6 12.4",
        ['L'] = "M1.6 1.8 V12.4 H6.6",
        ['M'] = "M1.2 12.4 V1.8 L4 8.2 L6.8 1.8 V12.4",
        ['N'] = "M1.4 12.4 V1.8 L6.6 12.4 V1.8",
        ['O'] = "M4 1.7 C1.4 1.7 1.2 12.5 4 12.5 C6.8 12.5 6.8 1.7 4 1.7",
        ['P'] = "M1.4 12.4 V1.8 H5.1 C7 1.8 7.1 7 5.1 7 H1.4",
        ['R'] = "M1.4 12.4 V1.8 H5.1 C7 1.8 7.1 7 5.1 7 H1.4 M4.2 7 L6.7 12.4",
        ['S'] = "M6.5 3.3 C5.6 1.9 2.2 1.7 2.2 4.8 C2.2 7.1 6 7.1 6 9.6 C6 12.6 2.1 12.4 1.6 10.6",
        ['T'] = "M1.2 1.8 H6.8 M4 1.8 V12.4",
        ['U'] = "M1.4 1.8 V9.4 C1.4 12.4 6.6 12.4 6.6 9.4 V1.8",
        ['V'] = "M1.2 1.8 L4 12.4 L6.8 1.8",
        ['W'] = "M1 1.8 L2.3 12.4 L4 6.6 L5.7 12.4 L7 1.8",
        ['X'] = "M1.4 1.8 L6.6 12.4 M6.6 1.8 L1.4 12.4",
        ['Y'] = "M1.3 1.8 L4 7.2 L6.7 1.8 M4 7.2 V12.4",
        ['Z'] = "M1.3 1.8 H6.7 L1.3 12.4 H6.7",
    };

    internal static void WriteAll(string output)
    {
        Directory.CreateDirectory(output);
        foreach (var spec in Documents)
        {
            File.WriteAllText(Path.Combine(output, spec.FileName), DocumentSvg(spec), new UTF8Encoding(false));
        }

        foreach (var spec in Archives)
        {
            File.WriteAllText(Path.Combine(output, spec.FileName), ArchiveSvg(spec), new UTF8Encoding(false));
        }
    }

    private static string DocumentSvg(DocumentSpec spec)
    {
        var decoration = spec.Decoration switch
        {
            Decoration.Grid => "<rect x=\"27\" y=\"52\" width=\"45\" height=\"34\" rx=\"6\" fill=\"none\" stroke=\"#9AA7B8\" stroke-width=\"4\"/><path d=\"M27 63h45M27 74h45M42 52v34M57 52v34\" stroke=\"#9AA7B8\" stroke-width=\"3\" opacity=\".72\"/>",
            Decoration.Slide => "<rect x=\"27\" y=\"51\" width=\"48\" height=\"35\" rx=\"7\" fill=\"#E4EAF2\"/><rect x=\"34\" y=\"58\" width=\"13\" height=\"19\" rx=\"4\" fill=\"#AAB5C5\"/><path d=\"M52 61h16M52 69h13M52 77h9\" stroke=\"#9AA7B8\" stroke-width=\"4\" stroke-linecap=\"round\"/>",
            Decoration.Code => "<path d=\"M39 55 29 65l10 10M61 55l10 10-10 10M54 50 46 80\" fill=\"none\" stroke=\"#96A3B5\" stroke-width=\"5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
            Decoration.Book => "<path d=\"M27 54c9-3 17-1 24 5v29c-7-6-15-8-24-5V54Zm48 0c-9-3-17-1-24 5v29c7-6 15-8 24-5V54Z\" fill=\"#DDE4EE\" stroke=\"#9CA9BA\" stroke-width=\"3\" stroke-linejoin=\"round\"/>",
            Decoration.Database => "<path d=\"M28 59c0-6 10-11 23-11s23 5 23 11v24c0 7-10 12-23 12S28 90 28 83V59Z\" fill=\"#E0E7EF\" stroke=\"#9CA9B8\" stroke-width=\"4\"/><ellipse cx=\"51\" cy=\"59\" rx=\"23\" ry=\"11\" fill=\"#EEF2F7\" stroke=\"#9CA9B8\" stroke-width=\"4\"/><path d=\"M28 71c4 6 12 9 23 9s19-3 23-9\" fill=\"none\" stroke=\"#9CA9B8\" stroke-width=\"3\"/>",
            Decoration.Settings => "<path d=\"M28 56h43M28 69h43M28 82h43\" stroke=\"#9BA8B8\" stroke-width=\"5\" stroke-linecap=\"round\"/><circle cx=\"43\" cy=\"56\" r=\"6\" fill=\"#E9EEF4\" stroke=\"#9BA8B8\" stroke-width=\"4\"/><circle cx=\"58\" cy=\"69\" r=\"6\" fill=\"#E9EEF4\" stroke=\"#9BA8B8\" stroke-width=\"4\"/><circle cx=\"39\" cy=\"82\" r=\"6\" fill=\"#E9EEF4\" stroke=\"#9BA8B8\" stroke-width=\"4\"/>",
            Decoration.System => "<circle cx=\"51\" cy=\"69\" r=\"21\" fill=\"#E2E8F0\"/><path d=\"M51 54v30M36 69h30M40 58l22 22M62 58 40 80\" stroke=\"#96A3B3\" stroke-width=\"5\" stroke-linecap=\"round\"/><circle cx=\"51\" cy=\"69\" r=\"8\" fill=\"#F8FAFC\" stroke=\"#96A3B3\" stroke-width=\"4\"/>",
            Decoration.Generic => "<rect x=\"28\" y=\"52\" width=\"45\" height=\"34\" rx=\"9\" fill=\"#E1E7EF\"/><rect x=\"36\" y=\"60\" width=\"10\" height=\"10\" rx=\"3\" fill=\"#9CA8B7\"/><rect x=\"54\" y=\"60\" width=\"10\" height=\"10\" rx=\"3\" fill=\"#9CA8B7\"/><rect x=\"36\" y=\"76\" width=\"28\" height=\"5\" rx=\"2.5\" fill=\"#9CA8B7\"/>",
            _ => "<path d=\"M28 56h43M28 69h37M28 82h29\" stroke=\"#9AA7B8\" stroke-width=\"6\" stroke-linecap=\"round\" opacity=\".82\"/>",
        };
        return $$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128" fill="none">
              <defs>
                <linearGradient id="paper" x1="18" y1="8" x2="105" y2="123" gradientUnits="userSpaceOnUse">
                  <stop stop-color="#FFFFFF"/>
                  <stop offset="1" stop-color="#E5EAF1"/>
                </linearGradient>
              </defs>
              <path d="M24 10h50l31 31v67c0 10-8 18-18 18H24c-10 0-18-8-18-18V28c0-10 8-18 18-18Z" fill="#64748B" opacity=".16"/>
              <path d="M24 6h50l31 31v67c0 10-8 18-18 18H24c-10 0-18-8-18-18V24c0-10 8-18 18-18Z" fill="url(#paper)"/>
              <path d="M74 6v31h31L74 6Z" fill="#D7DEE8"/>
              <path d="M75 8v27h27" fill="none" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round" opacity=".72"/>
              <g transform="translate(5 0)">{{decoration}}</g>
              <rect x="49" y="83" width="62" height="34" rx="11" fill="{{spec.Accent}}"/>
              {{LabelMarkup(spec.Label, 54, 89, 52, 22)}}
            </svg>
            """;
    }

    private static string ArchiveSvg(ArchiveSpec spec)
    {
        return $$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128" fill="none">
              <defs>
                <linearGradient id="pack" x1="25" y1="19" x2="104" y2="111" gradientUnits="userSpaceOnUse">
                  <stop stop-color="#FFD978"/>
                  <stop offset="1" stop-color="#F4A525"/>
                </linearGradient>
              </defs>
              <rect x="15" y="12" width="98" height="106" rx="25" fill="#B96E13" opacity=".14" transform="translate(0 4)"/>
              <rect x="15" y="12" width="98" height="106" rx="25" fill="url(#pack)"/>
              <g id="compact-mark">
                <rect x="51" y="29" width="47" height="58" rx="11" fill="#FFF4D5" fill-opacity=".72"/>
                <rect x="34" y="36" width="49" height="60" rx="12" fill="#FFF9E9"/>
                <path d="M44 52h29M44 66h21M44 80h29" stroke="#E58F11" stroke-width="7" stroke-linecap="round"/>
              </g>
              <rect x="56" y="84" width="55" height="31" rx="10" fill="{{spec.Accent}}"/>
              {{LabelMarkup(spec.Label, 60, 90, 47, 20)}}
            </svg>
            """;
    }

    private static string LabelMarkup(string label, float x, float y, float maxWidth, float maxHeight)
    {
        label = label.ToUpperInvariant();
        const float glyphWidth = 8f;
        const float glyphHeight = 14f;
        const float gap = 1.8f;
        var contentWidth = (label.Length * glyphWidth) + (Math.Max(0, label.Length - 1) * gap);
        var scale = MathF.Min(maxWidth / contentWidth, maxHeight / glyphHeight);
        var originX = x + ((maxWidth - (contentWidth * scale)) * 0.5f);
        var originY = y + ((maxHeight - (glyphHeight * scale)) * 0.5f);
        var stroke = Math.Clamp(1.85f * scale, 1.45f, 2.35f);
        var markup = new StringBuilder();
        markup.Append("<g id=\"badge-label\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"")
            .Append(Number(stroke))
            .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
        for (var index = 0; index < label.Length; index++)
        {
            if (!StrokeGlyphs.TryGetValue(label[index], out var path))
            {
                continue;
            }

            var left = originX + (index * (glyphWidth + gap) * scale);
            markup.Append("<g transform=\"translate(")
                .Append(Number(left))
                .Append(' ')
                .Append(Number(originY))
                .Append(") scale(")
                .Append(Number(scale))
                .Append(")\"><path d=\"")
                .Append(path)
                .Append("\"/></g>");
        }

        markup.Append("</g>");
        return markup.ToString();
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
