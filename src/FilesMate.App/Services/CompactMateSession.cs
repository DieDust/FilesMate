using FilesMate.App.Commands;
using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;

namespace FilesMate.App.Services;

internal static class CompactMateSession
{
    private static readonly CompactMateAvailability Availability = new(() =>
        OperatingSystem.IsWindows() && CompactMateHost.TryFind(new CurrentUserRegistry(), out _));

    public static Task<bool> IsAvailableAsync(bool forceRefresh = false) => Availability.GetAsync(forceRefresh);

    public static bool RequiresExternalProvider(AppCommandId id) =>
        id is AppCommandId.Compress7z or AppCommandId.OpenInCompactMate;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void Launch(CompactMateVerb verb, IReadOnlyList<string> paths, IUserRegistry registry)
    {
        if (!TryLaunch(verb, paths, registry))
            throw new IOException(StringTable.Get("Error_CompactMateMissing"));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool TryLaunch(CompactMateVerb verb, IReadOnlyList<string> paths, IUserRegistry registry) =>
        TryLaunch(verb, paths, registry,
            source => CompactMateHost.TryFind(source, out var executable) ? executable : null,
            launch => FilesMate.Platform.Windows.Processes.DetachedProcess.Open(
                launch.FileName, launch.Arguments, Path.GetDirectoryName(launch.FileName)));

    internal static bool TryLaunch(
        CompactMateVerb verb,
        IReadOnlyList<string> paths,
        IUserRegistry registry,
        Func<IUserRegistry, string?> findExecutable,
        Action<CompactMateLaunch> start)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(findExecutable);
        ArgumentNullException.ThrowIfNull(start);
        if (paths.Count == 0)
        {
            throw new IOException(StringTable.Get("Archive_InvalidSelection"));
        }

        // Always rediscover for an actual operation. The menu cache is only a
        // presentation hint and may predate an installation or uninstallation.
        var executable = findExecutable(registry);
        if (string.IsNullOrEmpty(executable)) return false;

        var launch = CompactMateHost.Create(executable, verb, paths);
        // A found provider that fails to start is an error, never a reason to
        // repeat an operation through another implementation.
        start(launch);
        return true;
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

/// <summary>Short-lived menu hint; never used to choose the execution route.</summary>
internal sealed class CompactMateAvailability(Func<bool> probe, TimeProvider? timeProvider = null)
{
    private readonly object _sync = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private Task<bool>? _pending;
    private DateTimeOffset _refreshAfter;

    public Task<bool> GetAsync(bool forceRefresh = false)
    {
        lock (_sync)
        {
            if (_pending is { } pending && (!pending.IsCompleted || (!forceRefresh && _time.GetUtcNow() < _refreshAfter)))
                return pending;
            _refreshAfter = _time.GetUtcNow() + TimeSpan.FromSeconds(2);
            // Registry reads and candidate File.Exists calls must not run on
            // the UI thread while a context menu is opening.
            return _pending = Task.Run(probe);
        }
    }
}
