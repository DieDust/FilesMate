using System.Diagnostics;
using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Directories;

namespace FilesMate.App.Services;

internal sealed class RecursiveCompatibilityException(string message) : IOException(message);

/// <summary>Additional fail-closed checks only for transfers originating in or targeting recursive view.</summary>
internal static class RecursiveTransferCompatibility
{
    internal static async Task VerifyAsync(IReadOnlyList<string> sources, string destination, CancellationToken cancellationToken = default, bool inspectWholeDestination = false)
    {
        var captured = sources.ToArray();
        var safe = await Task.Run(() => Verify(captured, destination, cancellationToken, inspectWholeDestination), cancellationToken);
        if (!safe) throw new RecursiveCompatibilityException(StringTable.Get("Recursive_OperationUnavailable"));
    }

    internal static async Task VerifySourcesAsync(IReadOnlyList<string> sources, CancellationToken token = default)
    {
        var captured = sources.ToArray();
        if (!await Task.Run(() => Verify(captured, null, token, false), token))
            throw new RecursiveCompatibilityException(StringTable.Get("Recursive_OperationUnavailable"));
    }

    private static bool Verify(IReadOnlyList<string> sources, string? destination, CancellationToken token, bool inspectWholeDestination)
    {
        var probe = new WindowsRecursiveDirectoryProbe();
        var checkedDirectories = new HashSet<string>(StringComparer.Ordinal);
        var compatibleDirectories = new HashSet<string>(StringComparer.Ordinal);
        var clock = Stopwatch.StartNew();
        try
        {
            if (destination is not null && !Compatible(destination)) return false;
            if (inspectWholeDestination && destination is not null && !CheckTree(destination)) return false;
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                if (clock.Elapsed > TimeSpan.FromSeconds(10)) return false;
                var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source));
                if (parent is null || !Compatible(parent)) return false;
                var attributes = File.GetAttributes(source);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) return false;
                if ((attributes & FileAttributes.Directory) != 0 && !CheckTree(source)) return false;
                if (destination is not null)
                {
                    var target = Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
                    if (Directory.Exists(target) && !CheckTree(target)) return false;
                }
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return false; }

        bool Compatible(string path)
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(10) || compatibleDirectories.Count >= 200_000) return false;
            if (compatibleDirectories.Contains(path)) return true;
            if (!probe.IsLegacyOperationCompatible(path, token)) return false;
            compatibleDirectories.Add(path);
            return true;
        }

        bool CheckTree(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var path))
            {
                token.ThrowIfCancellationRequested();
                if (!checkedDirectories.Add(path)) continue;
                // This validation must not become an unbounded extra filesystem crawl.
                if (checkedDirectories.Count > 200_000 || clock.Elapsed > TimeSpan.FromSeconds(10)) return false;
                if (!Compatible(path)) return false;
                foreach (var child in Directory.EnumerateDirectories(path, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0,
                    ReturnSpecialDirectories = false,
                }))
                {
                    token.ThrowIfCancellationRequested();
                    if (clock.Elapsed > TimeSpan.FromSeconds(10) || pending.Count + checkedDirectories.Count >= 200_000) return false;
                    pending.Push(child);
                }
            }
            return true;
        }
    }
}
