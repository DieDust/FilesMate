namespace FilesMate.Core.Operations;

public enum FileDropOperation { None, Copy, Move }

public static class FileDropPolicy
{
    public static FileDropOperation ResolveOperation(
        IReadOnlyList<string> sources, string? destination, bool validTarget,
        bool control, bool shift)
    {
        if (!validTarget || string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination))
            return FileDropOperation.None;
        var copy = control || (!shift && (sources.Count == 0
            || sources.Any(source => !string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination),
                StringComparison.OrdinalIgnoreCase))));
        if (sources.Count > 0 && FilterSources(sources, destination, !copy, allowSameDirectoryCopy: control).Count == 0)
            return FileDropOperation.None;
        return copy ? FileDropOperation.Copy : FileDropOperation.Move;
    }

    public static IReadOnlyList<string> FilterSources(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        bool move,
        bool allowSameDirectoryCopy = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var destination = Normalize(destinationDirectory);
        var result = new List<string>(sources.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Normalize(source);
            }
            catch (Exception error) when (error is ArgumentException
                or NotSupportedException
                or System.Security.SecurityException
                or PathTooLongException)
            {
                continue;
            }

            if (!seen.Add(candidate) || string.Equals(candidate, destination, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if ((move || !allowSameDirectoryCopy) && IsImmediateParent(destination, candidate))
            {
                continue;
            }

            if (IsNestedWithin(destination, candidate))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsImmediateParent(string destination, string source) =>
        string.Equals(
            Path.GetDirectoryName(source),
            destination,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsNestedWithin(string destination, string possibleParent)
    {
        var prefix = Path.EndsInDirectorySeparator(possibleParent)
            ? possibleParent
            : possibleParent + Path.DirectorySeparatorChar;
        return destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
