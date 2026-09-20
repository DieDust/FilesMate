namespace FilesMate.Platform.Windows.Locks;

public static class FileLockPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var value = path.Trim();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            value = @"\\" + value[8..];
        }
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        if (value.Length >= 2 && value[1] == ':')
        {
            value = char.ToUpperInvariant(value[0]) + value[1..];
        }

        return value.TrimEnd('\\', '/');
    }

    public static bool Matches(string candidate, string target, bool directory)
    {
        var left = Normalize(candidate);
        var right = Normalize(target);
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return directory
            && left.StartsWith(right + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
