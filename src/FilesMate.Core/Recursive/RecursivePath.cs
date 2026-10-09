namespace FilesMate.Core.Recursive;

internal static class RecursivePath
{
    internal static void ValidateLocalRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0')) throw new ArgumentException("Root path contains a null character.", nameof(path));
        var normalized = NormalizeForComparison(path);
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal) || normalized.StartsWith("//", StringComparison.Ordinal)
            || (IsWindows(path) && !(normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'
                && (normalized.Length >= 3 && normalized[2] == '\\'
                    || normalized.Length == 2 && IsSeparator(path[^1]))))
            || (!IsWindows(path) && !Path.IsPathFullyQualified(path)))
            throw new ArgumentException("Recursive browsing requires an absolute local directory root.", nameof(path));
        if (path.Split(['/', '\\']).Any(part => part is "." or ".."))
            throw new ArgumentException("Normalize the directory root before starting recursion.", nameof(path));
        if (IsWindows(path) && normalized[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => !IsSafeName(part, windows: true)))
            throw new ArgumentException("The directory root has an ambiguous Windows path component.", nameof(path));
    }

    internal static bool IsWindows(string path) => path.Contains('\\') || (path.Length >= 2 && path[1] == ':');
    internal static char Separator(string path) => IsWindows(path) ? '\\' : '/';
    internal static StringComparison Comparison(string path) => IsWindows(path)
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static string Combine(string parent, string name) =>
        IsSeparator(parent[^1]) ? parent + name : parent + Separator(parent) + name;
    internal static bool IsSeparator(char value) => value is '/' or '\\';
    internal static bool IsSafeName(string? name, bool windows)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0', ':']) >= 0)
            return false;
        if (!windows) return true;
        // Extended APIs can report names that ordinary Win32 paths normalize into another target
        // (notably ".. ") or a device. Do not expose an ambiguous operation identity in this mode.
        if (name[^1] is '.' or ' ' || name.IndexOfAny(['"', '<', '>', '|', '?', '*']) >= 0 || name.Any(c => c < 32))
            return false;
        var dot = name.IndexOf('.');
        var stem = (dot < 0 ? name.AsSpan() : name.AsSpan(0, dot)).TrimEnd();
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return false;
        return !(stem.Length == 4 && (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase)
            || stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase))
            && (stem[3] is >= '0' and <= '9' or '¹' or '²' or '³'));
    }

    internal static string NormalizeForComparison(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        if (IsWindows(path)) path = path.Replace('/', '\\');
        return path.TrimEnd('/', '\\');
    }

    internal static bool Equals(string left, string right) => string.Equals(
        NormalizeForComparison(left), NormalizeForComparison(right), Comparison(left));

    internal static bool IsDescendant(string parent, string candidate)
    {
        var left = NormalizeForComparison(parent);
        var right = NormalizeForComparison(candidate);
        return right.Length > left.Length && right.StartsWith(left, Comparison(parent))
            && IsSeparator(right[left.Length]);
    }
}
