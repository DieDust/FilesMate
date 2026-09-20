using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.Processes;

namespace FilesMate.Platform.Windows.CompactMate;

public readonly record struct CompactMateLaunch(string FileName, string Arguments);

public static class CompactMateHost
{
    public const string ArchiveOpenCommandKey =
        @"Software\Classes\CompactMate.Archive\shell\open\command";

    public const string AppPathKey =
        @"Software\Microsoft\Windows\CurrentVersion\App Paths\CompactMate.exe";

    public static bool TryFind(IUserRegistry registry, out string executable) =>
        TryFind(registry, extraCandidates: null, out executable);

    public static bool TryFind(
        IUserRegistry registry,
        IEnumerable<string>? extraCandidates,
        out string executable,
        bool includeNearby = true)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var candidate in Candidates(registry, extraCandidates, includeNearby))
        {
            if (File.Exists(candidate))
            {
                executable = Path.GetFullPath(candidate);
                return true;
            }
        }

        executable = string.Empty;
        return false;
    }

    public static CompactMateLaunch Create(
        string executable,
        CompactMateVerb verb,
        IReadOnlyList<string> paths,
        string? destination = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("CompactMate needs at least one path.", nameof(paths));
        }

        var args = new List<string>();
        if (verb != CompactMateVerb.Open)
        {
            args.Add("--verb");
            args.Add(VerbName(verb));
        }

        if (paths.Count == 1)
        {
            args.Add(paths[0]);
        }
        else
        {
            args.Add("--item-list");
            args.Add(WriteItemList(paths));
        }

        if (!string.IsNullOrWhiteSpace(destination))
        {
            args.Add("--destination");
            args.Add(destination);
        }

        return new CompactMateLaunch(executable, QuoteArguments(args));
    }

    public static string VerbName(CompactMateVerb verb) => verb switch
    {
        CompactMateVerb.ExtractHere => "ExtractHere",
        CompactMateVerb.ExtractToFolder => "ExtractToFolder",
        CompactMateVerb.ExtractToOther => "ExtractToOther",
        CompactMateVerb.SmartExtract => "SmartExtract",
        CompactMateVerb.CompressZip => "CompressZip",
        CompactMateVerb.Compress7z => "Compress7z",
        CompactMateVerb.CompressNew => "CompressNew",
        CompactMateVerb.Open => "Open",
        _ => throw new ArgumentOutOfRangeException(nameof(verb)),
    };

    public static string? ParseExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    private static IEnumerable<string> Candidates(
        IUserRegistry registry,
        IEnumerable<string>? extraCandidates,
        bool includeNearby)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> yieldUnique(IEnumerable<string?> values)
        {
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                {
                    continue;
                }

                yield return value;
            }
        }

        foreach (var path in yieldUnique(
            [
                ParseExecutable(registry.GetDefaultValue(ArchiveOpenCommandKey)),
                ParseExecutable(registry.GetDefaultValue(AppPathKey)),
                registry.GetValue(AppPathKey, "Path") is string dir && !string.IsNullOrWhiteSpace(dir)
                    ? Path.Combine(dir, "CompactMate.exe")
                    : null,
            ]))
        {
            yield return path;
        }

        if (extraCandidates is not null)
        {
            foreach (var path in yieldUnique(extraCandidates))
            {
                yield return path;
            }
        }

        if (!includeNearby)
        {
            yield break;
        }

        foreach (var path in yieldUnique(NearbyExecutables()))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> NearbyExecutables()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "CompactMate",
            "CompactMate.exe");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "CompactMate",
            "CompactMate.exe");

        var start = Environment.ProcessPath;
        if (string.IsNullOrEmpty(start))
        {
            yield break;
        }

        var dir = new DirectoryInfo(Path.GetDirectoryName(start) ?? start);
        for (var depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "CompactMate.exe");
            if (dir.Parent is not null)
            {
                yield return Path.Combine(dir.Parent.FullName, "CompactMate", "CompactMate.exe");
            }
        }
    }

    private static string WriteItemList(IReadOnlyList<string> paths)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CompactMate");
        Directory.CreateDirectory(folder);
        var list = Path.Combine(folder, "items-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(list, paths.Select(Path.GetFullPath));
        return list;
    }

    private static string QuoteArguments(IReadOnlyList<string> arguments)
    {
        return WindowsCommandLine.JoinArguments(arguments);
    }
}
