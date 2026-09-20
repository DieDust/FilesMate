using FilesMate.App.Commands;
using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;

namespace FilesMate.App.Services;

internal static class CompactMateSession
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void Launch(CompactMateVerb verb, IReadOnlyList<string> paths, IUserRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(registry);
        if (paths.Count == 0)
        {
            throw new IOException(StringTable.Get("Error_NoFolder"));
        }

        if (!CompactMateHost.TryFind(registry, out var executable))
        {
            throw new IOException(StringTable.Get("Error_CompactMateMissing"));
        }

        var launch = CompactMateHost.Create(executable, verb, paths);
        FilesMate.Platform.Windows.Processes.DetachedProcess.Open(
            launch.FileName, launch.Arguments, Path.GetDirectoryName(launch.FileName));
    }

    public static CompactMateVerb? VerbFor(AppCommandId id) => id switch
    {
        AppCommandId.ExtractHere => CompactMateVerb.ExtractHere,
        AppCommandId.ExtractToFolder => CompactMateVerb.ExtractToFolder,
        AppCommandId.ExtractToOther => CompactMateVerb.ExtractToOther,
        AppCommandId.SmartExtract => CompactMateVerb.SmartExtract,
        AppCommandId.CompressZip => CompactMateVerb.CompressZip,
        AppCommandId.Compress7z => CompactMateVerb.Compress7z,
        AppCommandId.CompressNew => CompactMateVerb.CompressNew,
        AppCommandId.OpenInCompactMate => CompactMateVerb.Open,
        _ => null,
    };
}
