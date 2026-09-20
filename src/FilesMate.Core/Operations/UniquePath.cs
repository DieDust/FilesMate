namespace FilesMate.Core.Operations;

public static class UniquePath
{
    public static string CombineAvailable(string directory, string fileName, Func<string, bool> exists, bool isDirectory = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(exists);

        var candidate = Path.Combine(directory, fileName);
        if (!exists(candidate))
        {
            return candidate;
        }

        var stem = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);
        var extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
        for (var n = 2; n < 10_000; n++)
        {
            candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not allocate a unique name for '{fileName}'.");
    }
}
