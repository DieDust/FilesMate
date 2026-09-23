namespace FilesMate.App.Models;

public enum SearchHitKind
{
    Program,
    Shortcut,
    Folder,
    Document,
    Image,
    Video,
    Audio,
    Archive,
    Code,
    Other,
    Executable,
}

public static class SearchHitKinds
{
    public static IReadOnlyList<SearchHitKind> FromSavedOrder(IEnumerable<SearchHitKind>? order, int version)
    {
        var saved = order?.ToArray();
        SearchHitKind[] legacy = [SearchHitKind.Program, SearchHitKind.Shortcut, SearchHitKind.Folder,
            SearchHitKind.Document, SearchHitKind.Image, SearchHitKind.Video, SearchHitKind.Audio,
            SearchHitKind.Archive, SearchHitKind.Code, SearchHitKind.Other];
        SearchHitKind[] previous = [SearchHitKind.Shortcut, SearchHitKind.Program, SearchHitKind.Document,
            SearchHitKind.Image, SearchHitKind.Video, SearchHitKind.Audio, SearchHitKind.Archive,
            SearchHitKind.Code, SearchHitKind.Other, SearchHitKind.Folder];
        SearchHitKind[] lastDefault = [SearchHitKind.Program, SearchHitKind.Executable, SearchHitKind.Document,
            SearchHitKind.Image, SearchHitKind.Video, SearchHitKind.Audio, SearchHitKind.Archive,
            SearchHitKind.Code, SearchHitKind.Other, SearchHitKind.Shortcut, SearchHitKind.Folder];
        if (saved is not null && (version < 2 && saved.SequenceEqual(legacy) || version < 3 && saved.SequenceEqual(previous)))
            return DefaultOrder;
        if (saved is not null && version < 5 && saved.SequenceEqual(lastDefault))
            return DefaultOrder;
        if (saved is not null && !saved.Contains(SearchHitKind.Executable))
        {
            var migrated = SanitizeOrder(saved).Where(kind => kind != SearchHitKind.Executable).ToList();
            migrated.Insert(migrated.IndexOf(SearchHitKind.Program) + 1, SearchHitKind.Executable);
            return migrated;
        }
        return SanitizeOrder(saved);
    }
    public static IReadOnlyList<SearchHitKind> DefaultOrder { get; } =
    [
        SearchHitKind.Program,
        SearchHitKind.Executable,
        SearchHitKind.Folder,
        SearchHitKind.Document,
        SearchHitKind.Image,
        SearchHitKind.Video,
        SearchHitKind.Audio,
        SearchHitKind.Archive,
        SearchHitKind.Code,
        SearchHitKind.Shortcut,
        SearchHitKind.Other,
    ];

    public static string TitleKey(SearchHitKind kind) => kind switch
    {
        SearchHitKind.Program => "SearchRankProgram",
        SearchHitKind.Executable => "SearchRankExecutable",
        SearchHitKind.Shortcut => "SearchRankShortcut",
        SearchHitKind.Folder => "SearchRankFolder",
        SearchHitKind.Document => "SearchRankDocument",
        SearchHitKind.Image => "SearchRankImage",
        SearchHitKind.Video => "SearchRankVideo",
        SearchHitKind.Audio => "SearchRankAudio",
        SearchHitKind.Archive => "SearchRankArchive",
        SearchHitKind.Code => "SearchRankCode",
        _ => "SearchRankOther",
    };

    public static IReadOnlyList<SearchHitKind> SanitizeOrder(IEnumerable<SearchHitKind>? order)
    {
        var result = new List<SearchHitKind>(DefaultOrder.Count);
        var seen = new HashSet<SearchHitKind>();
        if (order is not null)
        {
            foreach (var kind in order)
            {
                if (Enum.IsDefined(kind) && seen.Add(kind))
                {
                    result.Add(kind);
                }
            }
        }

        foreach (var kind in DefaultOrder)
        {
            if (seen.Add(kind))
            {
                result.Add(kind);
            }
        }

        return result;
    }
}
