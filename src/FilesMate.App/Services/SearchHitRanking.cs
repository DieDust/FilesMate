using FilesMate.App.Icons;
using FilesMate.App.Models;

namespace FilesMate.App.Services;

public static class SearchHitRanking
{
    public static Func<string, bool, int> CreateRank(IEnumerable<SearchHitKind>? order, string query)
    {
        var priority = SearchHitKinds.SanitizeOrder(order).Select((kind, index) => (kind, index)).ToDictionary(pair => pair.kind, pair => pair.index);
        return (path, directory) =>
        {
            var kind = Classify(path, directory);
            // A verified application entry is more useful than its helper EXEs.
            // Ordinary document/folder links never receive this application boost.
            var entry = kind == SearchHitKind.Program && !FilesMate.Search.ApplicationShortcut.IsApplication(path) ? 4 : 0;
            return priority[kind] * 8 + entry + FilesMate.Search.NameIndexReader.Relevance(Path.GetFileName(path), query);
        };
    }
    public static SearchHitKind Classify(string path, bool directory) => Classify(path, directory, recognizeApplicationShortcuts: true);

    public static SearchHitKind Classify(string path, bool directory, bool recognizeApplicationShortcuts)
    {
        if (directory)
        {
            return SearchHitKind.Folder;
        }

        if (recognizeApplicationShortcuts && FilesMate.Search.ApplicationShortcut.IsApplication(path)) return SearchHitKind.Program;

        var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        if (extension is ".com")
        {
            return SearchHitKind.Program;
        }

        var icon = FileTypeIconCatalog.ClassifyPath(path, false);
        if (icon is null)
        {
            return SearchHitKind.Program;
        }

        return icon switch
        {
            FileIconKind.Folder => SearchHitKind.Folder,
            FileIconKind.Link => SearchHitKind.Shortcut,
            FileIconKind.Image => SearchHitKind.Image,
            FileIconKind.Audio => SearchHitKind.Audio,
            FileIconKind.Video => SearchHitKind.Video,
            FileIconKind.Archive or FileIconKind.Zip or FileIconKind.SevenZip or FileIconKind.Rar or FileIconKind.Tar
                => SearchHitKind.Archive,
            FileIconKind.Code or FileIconKind.JavaScript or FileIconKind.TypeScript or FileIconKind.CSharp
                or FileIconKind.Cpp or FileIconKind.Python or FileIconKind.Rust or FileIconKind.Go
                or FileIconKind.Java or FileIconKind.Yaml or FileIconKind.Toml or FileIconKind.Json
                or FileIconKind.Xml or FileIconKind.Html
                => SearchHitKind.Code,
            FileIconKind.Pdf or FileIconKind.Word or FileIconKind.Spreadsheet or FileIconKind.Presentation
                or FileIconKind.Text or FileIconKind.Markdown or FileIconKind.Csv or FileIconKind.Log
                or FileIconKind.Ebook or FileIconKind.Document
                => SearchHitKind.Document,
            _ => SearchHitKind.Other,
        };
    }

    public static IReadOnlyList<HomeSearchHit> Sort(
        IReadOnlyList<HomeSearchHit> hits,
        IEnumerable<SearchHitKind>? order,
        int limit, string? query = null)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (hits.Count == 0 || limit <= 0)
        {
            return [];
        }

        var rank = new Dictionary<SearchHitKind, int>();
        var sanitized = SearchHitKinds.SanitizeOrder(order);
        for (var i = 0; i < sanitized.Count; i++)
        {
            rank[sanitized[i]] = i;
        }

        return hits
            .OrderBy(hit => rank[Classify(hit.Path, hit.IsDirectory)])
            .ThenBy(hit => Classify(hit.Path, hit.IsDirectory) == SearchHitKind.Program && !FilesMate.Search.ApplicationShortcut.IsApplication(hit.Path) ? 1 : 0)
            .ThenBy(hit => query is null ? 0 : FilesMate.Search.NameIndexReader.Relevance(hit.Name, query))
            .ThenBy(hit => hit.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToArray();
    }
}
