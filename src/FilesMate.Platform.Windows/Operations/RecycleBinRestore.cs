using System.Text;

namespace FilesMate.Platform.Windows.Operations;

public static class RecycleBinRestore
{
    private static readonly EnumerationOptions HiddenSystem = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public static void Restore(IReadOnlyList<string> originalPaths)
    {
        ArgumentNullException.ThrowIfNull(originalPaths);
        if (originalPaths.Count == 0)
        {
            throw new ArgumentException("At least one path is required.", nameof(originalPaths));
        }

        var ordered = originalPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Canonical)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Depth(path))
            .ToArray();

        foreach (var original in ordered)
        {
            RestoreOne(original);
        }
    }

    public static bool TryReadOriginalPath(string infoFile, out string originalPath, out long deletedUtcTicks)
    {
        originalPath = "";
        deletedUtcTicks = 0;
        try
        {
            using var stream = new FileStream(infoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Both known formats fit a Windows path. Never allocate from an
            // unchecked file length or silently resolve a relative restore path.
            if (stream.Length < 28 || stream.Length > 28 + 32768 * 2)
            {
                return false;
            }

            using var reader = new BinaryReader(stream);
            var version = reader.ReadInt64();
            reader.ReadInt64();
            var deleted = reader.ReadInt64();
            string? path;
            if (version == 1)
            {
                if (stream.Length != 544) return false;
                var bytes = reader.ReadBytes(520);
                path = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            else if (version == 2)
            {
                var count = reader.ReadInt32();
                if (count < 2 || count > 32768 || stream.Length - stream.Position != count * 2L) return false;
                var value = Encoding.Unicode.GetString(reader.ReadBytes(count * 2));
                if (value[^1] != '\0') return false;
                path = value[..^1];
            }
            else return false;

            if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            originalPath = Canonical(path);
            deletedUtcTicks = deleted;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void RestoreOne(string originalPath)
    {
        if (Path.Exists(originalPath))
        {
            throw new IOException($"Could not restore '{originalPath}' because something already uses that name.");
        }

        if (!TryFindRecycled(originalPath, out var dataPath, out var infoPath))
        {
            throw new IOException($"Could not find '{originalPath}' in the Recycle Bin.");
        }

        var parent = Path.GetDirectoryName(originalPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (Directory.Exists(dataPath))
        {
            Directory.Move(dataPath, originalPath);
        }
        else
        {
            File.Move(dataPath, originalPath);
        }

        try
        {
            if (File.Exists(infoPath))
            {
                File.Delete(infoPath);
            }
        }
        catch (Exception)
        {
            // ponytail: the restored file is the user-visible result; leftover $I is Recycle Bin chrome.
        }

        ClearRecycleAttributes(originalPath);
    }

    private static bool TryFindRecycled(string originalPath, out string dataPath, out string infoPath)
    {
        dataPath = "";
        infoPath = "";
        var root = Path.GetPathRoot(originalPath);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var bin = Path.Combine(root, "$Recycle.Bin");
        if (!Directory.Exists(bin))
        {
            return false;
        }

        string? bestData = null;
        string? bestInfo = null;
        var bestDeleted = long.MinValue;
        foreach (var sidDir in Directory.EnumerateDirectories(bin, "*", HiddenSystem))
        {
            IEnumerable<string> infoFiles;
            try
            {
                infoFiles = Directory.EnumerateFiles(sidDir, "$I*", HiddenSystem);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var candidate in infoFiles)
            {
                if (!TryReadOriginalPath(candidate, out var restored, out var deleted)
                    || !string.Equals(restored, originalPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileName(candidate);
                if (name.Length < 3)
                {
                    continue;
                }

                var data = Path.Combine(sidDir, "$R" + name[2..]);
                if (!Path.Exists(data) || deleted < bestDeleted)
                {
                    continue;
                }

                bestDeleted = deleted;
                bestData = data;
                bestInfo = candidate;
            }
        }

        if (bestData is null || bestInfo is null)
        {
            return false;
        }

        dataPath = bestData;
        infoPath = bestInfo;
        return true;
    }

    private static void ClearRecycleAttributes(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            var cleaned = attributes & ~(FileAttributes.Hidden | FileAttributes.System);
            if (cleaned != attributes)
            {
                File.SetAttributes(path, cleaned);
            }
        }
        catch (Exception)
        {
            // ponytail: restored path matters more than recycle-bin Hidden/System bits.
        }
    }

    private static string Canonical(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static int Depth(string path)
    {
        var depth = 0;
        foreach (var c in path)
        {
            if (c is '\\' or '/')
            {
                depth++;
            }
        }

        return depth;
    }
}
