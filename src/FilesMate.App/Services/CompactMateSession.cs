using System.Diagnostics;

using FilesMate.App.Commands;
using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;

namespace FilesMate.App.Services;

internal static class CompactMateSession
{
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
        Process.Start(new ProcessStartInfo
        {
            FileName = launch.FileName,
            Arguments = launch.Arguments,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launch.FileName),
        });
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
