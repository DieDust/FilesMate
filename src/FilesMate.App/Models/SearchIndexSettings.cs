namespace FilesMate.App.Models;

public sealed record SearchIndexSettings(
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> Exclusions,
    bool ScanInParallel,
    int MaxDepth,
    string DatabaseDirectory,
    bool AutoRefresh,
    IReadOnlyList<SearchHitKind> RankOrder)
{
    public const string DatabaseFileName = "search-index.db";

    public const int DefaultMaxDepth = 6;

    public const int MaxAllowedDepth = 32;

    public static string DefaultDatabaseDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilesMate");

    public static SearchIndexSettings Default => Sanitize(
        roots: null,
        exclusions: null,
        scanInParallel: false);

    public string ResolveDatabasePath() => Path.Combine(DatabaseDirectory, DatabaseFileName);

    public static SearchIndexSettings Sanitize(
        IEnumerable<string>? roots,
        IEnumerable<string>? exclusions,
        bool? scanInParallel,
        int? maxDepth = null,
        string? databaseDirectory = null,
        bool? autoRefresh = null,
        IEnumerable<SearchHitKind>? rankOrder = null)
    {
        var cleanRoots = roots is null ? AvailableDriveRoots() : DistinctExisting(roots);
        if (cleanRoots.Count == 0)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile) && Directory.Exists(profile))
            {
                cleanRoots.Add(Path.GetFullPath(profile));
            }
        }

        var cleanExclusions = DistinctTokens(exclusions);
        if (cleanExclusions.Count == 0)
        {
            cleanExclusions.AddRange(SearchIndexPathRules.DefaultExclusions);
        }

        var depth = maxDepth ?? DefaultMaxDepth;
        if (depth < 0)
        {
            depth = 0;
        }
        else if (depth > MaxAllowedDepth)
        {
            depth = MaxAllowedDepth;
        }

        return new(
            cleanRoots,
            cleanExclusions,
            scanInParallel ?? false,
            depth,
            SanitizeDirectory(databaseDirectory),
            autoRefresh ?? true,
            SearchHitKinds.SanitizeOrder(rankOrder));
    }

    private static string SanitizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return DefaultDatabaseDirectory;
        }

        try
        {
            var full = Path.GetFullPath(directory.Trim().Trim('"'));
            if (full.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return DefaultDatabaseDirectory;
            }

            if (File.Exists(full) && !Directory.Exists(full))
            {
                return DefaultDatabaseDirectory;
            }

            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DefaultDatabaseDirectory;
        }
    }

    private static List<string> AvailableDriveRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    // Do not probe network shares or empty optical drives at startup.
                    if (drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
                    {
                        roots.Add(drive.RootDirectory.FullName);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A disconnected or locked drive must not hide the other disks.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return roots;
    }

    private static List<string> DistinctExisting(IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(value.Trim().Trim('"'));
                if (Directory.Exists(full) && seen.Add(full))
                {
                    result.Add(full);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return result;
    }

    private static List<string> DistinctTokens(IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            var token = value.Trim().Trim('"');
            if (token.Length > 0 && seen.Add(token))
            {
                result.Add(token);
            }
        }

        return result;
    }
}

public readonly record struct SearchIndexStats(long Files, long Folders, long Errors, DateTimeOffset? CompletedUtc)
{
    public static SearchIndexStats Empty { get; } = new(0, 0, 0, null);

    public long Indexed => Files + Folders;
}

public readonly record struct SearchIndexProgress(
    string? CurrentPath,
    long Files,
    long Folders,
    long Errors,
    bool Running)
{
    public bool Cancelled { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
}
