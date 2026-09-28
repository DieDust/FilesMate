using FilesMate.Platform.Windows.Archives;
using FilesMate.Platform.Windows.CompactMate;

namespace FilesMate.App.Services;

internal sealed record ArchiveRoute(ArchiveProvider Provider, string? Executable, bool UsedFallback);
internal sealed record ArchiveLaunch(string Executable, IReadOnlyList<string> Arguments);

internal static class ArchiveRouting
{
    internal static readonly ArchiveProvider[] AutomaticOrder = [ArchiveProvider.CompactMate, ArchiveProvider.Bandizip,
        ArchiveProvider.SevenZip, ArchiveProvider.WinRAR];

    internal static ArchiveRoute Choose(ArchivePreferences preferences, CompactMateVerb verb, IReadOnlyList<string> paths,
        bool decorated, Func<ArchiveProvider, string?, string?>? find = null)
    {
        find ??= ArchiveProviderDiscovery.Find;
        if (preferences.Preferred == ArchiveProvider.BuiltIn) return new(ArchiveProvider.BuiltIn, null, false);
        var candidates = preferences.Preferred == ArchiveProvider.Automatic ? AutomaticOrder : [preferences.Preferred];
        foreach (var provider in candidates)
        {
            var executable = find(provider, preferences.PathFor(provider));
            if (executable is not null && Supports(provider, executable, verb, paths, decorated))
                return new(provider, executable, false);
        }
        return new(ArchiveProvider.BuiltIn, null, preferences.Preferred != ArchiveProvider.Automatic);
    }

    internal static bool Supports(ArchiveProvider provider, string executable, CompactMateVerb verb,
        IReadOnlyList<string> paths, bool decorated)
    {
        if (provider == ArchiveProvider.CompactMate) return true;
        if (verb == CompactMateVerb.Open) return paths.Count == 1;
        // Renamed volumes require stream-based detection; recursive smart extraction belongs to CompactMate.
        if (decorated || verb == CompactMateVerb.SmartExtract) return false;
        if (provider is ArchiveProvider.Zip360 or ArchiveProvider.HaoZip or ArchiveProvider.Other) return false;
        // Do not launch a console utility invisibly, where password/conflict prompts would hang.
        var executableName = Path.GetFileNameWithoutExtension(executable);
        if (provider == ArchiveProvider.SevenZip && !executableName.Equals("7zG", StringComparison.OrdinalIgnoreCase)) return false;
        if (provider == ArchiveProvider.SevenZipCompatible && new[] { "7z", "7za", "7zz", "NanaZipC", "HaoZipC" }
            .Contains(executableName, StringComparer.OrdinalIgnoreCase)) return false;
        if (provider == ArchiveProvider.WinRAR && verb is CompactMateVerb.Compress7z or CompactMateVerb.CompressZip or CompactMateVerb.CompressNew) return false;
        if (paths.Sum(path => (long)path.Length + 4) > 24_000) return false;
        // Bandizip supports a single batch GUI; other providers use built-in batch handling.
        var compress = verb is CompactMateVerb.CompressZip or CompactMateVerb.Compress7z or CompactMateVerb.CompressNew;
        return compress || provider == ArchiveProvider.Bandizip || paths.Count == 1;
    }

    internal static ArchiveLaunch Create(ArchiveRoute route, CompactMateVerb verb, IReadOnlyList<string> paths,
        string destination, string archiveName)
    {
        if (paths.Count == 0 || route.Executable is null) throw new ArgumentException("Missing archive application or selection.");
        if (verb == CompactMateVerb.Open)
        {
            var executable = route.Executable;
            if (route.Provider == ArchiveProvider.SevenZip)
                executable = Path.Combine(Path.GetDirectoryName(executable)!, "7zFM.exe");
            else if (route.Provider == ArchiveProvider.SevenZipCompatible && Path.GetFileName(executable).Equals("7zG.exe", StringComparison.OrdinalIgnoreCase))
                executable = Path.Combine(Path.GetDirectoryName(executable)!, "7zFM.exe");
            return new(executable, [paths[0]]);
        }
        var compress = verb is CompactMateVerb.CompressZip or CompactMateVerb.Compress7z or CompactMateVerb.CompressNew;
        var format = archiveName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? "7z" : "zip";
        var args = new List<string>();
        switch (route.Provider)
        {
            case ArchiveProvider.Bandizip:
                // The dialog confirms archive creation; 'c' would silently replace existing archives.
                if (compress) args.AddRange(["cd", "-fmt:" + format, Path.Combine(destination, archiveName)]);
                else
                {
                    args.AddRange([paths.Count > 1 ? "bx" : "x", "-o:" + destination]);
                    if (verb == CompactMateVerb.ExtractToFolder) args.Add("-target:name");
                }
                args.AddRange(paths);
                break;
            case ArchiveProvider.SevenZip:
            case ArchiveProvider.SevenZipCompatible:
                if (compress) { args.AddRange(["a", "-ad", "-t" + format, "--", Path.Combine(destination, archiveName)]); args.AddRange(paths); }
                else args.AddRange(["x", "-o" + destination, "--", paths[0]]);
                break;
            case ArchiveProvider.WinRAR:
                args.AddRange(["x", paths[0], Path.TrimEndingDirectorySeparator(destination) + Path.DirectorySeparatorChar]);
                break;
            default: throw new NotSupportedException();
        }
        return new(route.Executable, args);
    }
}
