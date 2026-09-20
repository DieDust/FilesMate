namespace FilesMate.App.Models;

public static class SearchIndexPathRules
{
    public static IReadOnlyList<string> DefaultExclusions { get; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "WinSxS",
        "node_modules",
        "$Recycle.Bin",
        "System Volume Information",
        // Tool-owned trees: tens of thousands of names nobody searches for by hand, and they dominate scan time.
        ".git",
        ".vs",
        "__pycache__",
    ];

    public static bool IsExcluded(string path, IReadOnlyList<string> exclusions)
    {
        if (string.IsNullOrWhiteSpace(path) || exclusions.Count == 0)
        {
            return string.IsNullOrWhiteSpace(path);
        }

        foreach (var raw in exclusions)
        {
            var rule = raw.Trim().TrimEnd('\\', '/');
            if (rule.Length == 0)
            {
                continue;
            }

            if (Path.IsPathRooted(rule))
            {
                if (path.StartsWith(rule, StringComparison.OrdinalIgnoreCase)
                    && (path.Length == rule.Length || path[rule.Length] is '\\' or '/'))
                {
                    return true;
                }
            }
            else
            {
                var padded = path.EndsWith('\\') || path.EndsWith('/') ? path : path + "\\";
                if (padded.Contains("\\" + rule + "\\", StringComparison.OrdinalIgnoreCase)
                    || padded.Contains("/" + rule + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
