using FilesMate.Core.Directories;

namespace FilesMate.Core.Recursive;

public enum RecursiveSessionState { Loading, Completed, Partial, Failed, Cancelled, Paused }

public enum RecursiveChildState { NotDirectory, Unknown, Loading, HasChildren, Empty, Unavailable, Excluded, Error }

/// <summary>Current scan ownership, never the state of an older view publication.</summary>
public static class RecursiveScanActivity
{
    public static bool IsActive(RecursiveSessionState state, bool automaticContinuation, bool canContinue, bool stopped)
        => !stopped && (state == RecursiveSessionState.Loading
            || state == RecursiveSessionState.Paused && automaticContinuation && canContinue);
}

[Flags]
public enum RecursiveLimit { None = 0, Nodes = 1, Depth = 2, MemoryBudget = 4 }

/// <summary>Admission estimates cover retained scan/projection data, not measured process memory.</summary>
public readonly record struct RecursiveScanBudget(int NodeAllowance, long EstimatedRetainedBytes,
    long ByteAllowance, long MaximumByteAllowance, int PendingDirectories, bool CanContinue)
{
    public RecursiveMemoryEstimate Estimate { get; init; }
    public long SharedReservedBytes { get; init; }
    public long SharedCapacityBytes { get; init; }
    public bool SharedPressure { get; init; }
    public long EstimatedObservationBytes { get; init; }
}

public enum RecursiveTraversalState
{
    NotDirectory, Pending, Enumerating, Completed,
    SkippedReparsePoint, SkippedDepthLimit, SkippedNodeLimit, SkippedOffline, Failed,
}

[Flags]
public enum RecursiveNodeObservation : byte
{
    None = 0,
    CaseFoldCollision = 1,
    ListingOmittedEntries = 2,
}

public enum RecursiveOperationCompatibility : byte { NotRequired, Pending, Compatible, Unverified }

/// <summary>
/// Compact session-local identity. ParentId zero is the virtual root; direct children have Depth zero.
/// Full paths are reconstructed from immutable parent links only when needed, not retained per node.
/// </summary>
public readonly record struct RecursiveNode(int Id, int ParentId, int Depth, bool IsDirectory,
    bool IsReparsePoint, RecursiveTraversalState TraversalState)
{
    public RecursiveNodeObservation Observation { get; init; }
    public RecursiveOperationCompatibility OperationCompatibility { get; init; }
    /// <summary>Observed same-parent names compare equal ignoring case but are ordinally distinct.</summary>
    public bool HasCaseFoldCollision => (Observation & RecursiveNodeObservation.CaseFoldCollision) != 0;
    /// <summary>Only this directory's own listing is complete; descendants may still be unscanned.</summary>
    public bool ListingComplete => IsDirectory && TraversalState == RecursiveTraversalState.Completed
        && (Observation & RecursiveNodeObservation.ListingOmittedEntries) == 0;
    public bool TraversalSkipped => TraversalState is RecursiveTraversalState.SkippedReparsePoint
        or RecursiveTraversalState.SkippedDepthLimit or RecursiveTraversalState.SkippedNodeLimit or RecursiveTraversalState.SkippedOffline
        or RecursiveTraversalState.Failed;
}

/// <summary>Engineering budgets, not a claim that every filesystem operation completes within a deadline.</summary>
public sealed record RecursiveReadOptions
{
    public int MaximumNodes { get; init; } = 200_000;
    /// <summary>Pause with the current batch and frontier intact, instead of truncating the scan.</summary>
    public bool AllowContinuation { get; init; }
    /// <summary>Admit subsequent steps on the scan worker; wait asynchronously when shared headroom is low.</summary>
    public bool AutomaticContinuation { get; init; }
    public long InitialRetainedByteAllowance { get; init; } = 256L * 1024 * 1024;
    public long RetainedByteStep { get; init; } = 256L * 1024 * 1024;
    /// <summary>A finite admission ceiling. Trees beyond it need a paged store, not unlimited allocation.</summary>
    public long MaximumRetainedByteAllowance { get; init; } = 1024L * 1024 * 1024;
    /// <summary>Optional shared process admission; production panes use the same pool.</summary>
    public RecursiveMemoryBudgetPool? MemoryBudgetPool { get; init; }
    /// <summary>Exact-path view targets retained across a same-root refresh must enter the new reservation.</summary>
    public long InitialViewStateBytes { get; init; }
    /// <summary>Charge this bounded view-history headroom before scanning, so a paused tree remains expandable.</summary>
    public long MinimumViewStateReserveBytes { get; init; }
    /// <summary>Maximum number of entry levels below the root. Direct children consume the first level.</summary>
    public int MaximumDepth { get; init; } = 256;
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(200);
    public DirectoryReadOptions DirectoryOptions { get; init; } = DirectoryReadOptions.Default;
    /// <summary>Require a captured affirmative native compatibility check before legacy operations may use a directory.</summary>
    public bool RequireOperationCompatibilityVerification { get; init; }

    internal void Validate()
    {
        if (AutomaticContinuation && !AllowContinuation) throw new ArgumentException("Automatic continuation requires retained continuation state.", nameof(AutomaticContinuation));
        if (MaximumNodes is < 1 or > 2_000_000) throw new ArgumentOutOfRangeException(nameof(MaximumNodes));
        if (MaximumDepth is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(MaximumDepth));
        if (InitialRetainedByteAllowance < 1 || RetainedByteStep < 1
            || MaximumRetainedByteAllowance < InitialRetainedByteAllowance
            || MaximumRetainedByteAllowance > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumRetainedByteAllowance));
        if (InitialViewStateBytes < 0 || InitialViewStateBytes > MaximumRetainedByteAllowance / 3)
            throw new ArgumentOutOfRangeException(nameof(InitialViewStateBytes));
        if (MinimumViewStateReserveBytes < 0 || MinimumViewStateReserveBytes > InitialRetainedByteAllowance)
            throw new ArgumentOutOfRangeException(nameof(MinimumViewStateReserveBytes));
        if (ProgressInterval < TimeSpan.FromMilliseconds(16) || ProgressInterval > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(ProgressInterval));
        ArgumentNullException.ThrowIfNull(DirectoryOptions);
    }
}

public readonly record struct RecursiveSessionChange(RecursiveSnapshot Snapshot)
{
    public Guid SessionId => Snapshot.SessionId;
    public long Generation => Snapshot.Generation;
    public long Revision => Snapshot.Revision;
    public RecursiveSessionState State => Snapshot.State;
    public int EntryCount => Snapshot.Count;
    public int Count => Snapshot.Count;
    public int SkippedDirectories => Snapshot.SkippedDirectories;
    public int SkippedEntries => Snapshot.SkippedEntries;
    public bool LimitReached => Snapshot.LimitReached;
    public RecursiveLimit Limits => Snapshot.Limits;
    public DirectoryReadError? Error => Snapshot.Error;
}

/// <summary>
/// Rechecks directory attributes immediately before each enumeration. An injectable filesystem
/// seam permits synthetic tests without disabling the production reparse-point check.
/// This advisory check does not pin a Windows directory handle against concurrent replacement.
/// </summary>
public interface IRecursiveDirectoryProbe
{
    public FileAttributes GetAttributes(string path);
    public FileAttributes GetAttributes(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetAttributes(path);
    }
}

/// <summary>
/// Optional background-only filesystem capability seam for legacy case-insensitive operations.
/// False or a probe failure leaves the tree readable but never grants operation compatibility.
/// </summary>
public interface IRecursiveOperationCompatibilityProbe
{
    public bool IsLegacyOperationCompatible(string path, CancellationToken cancellationToken);
}

/// <summary>
/// Shared production preflight for both the recursive session and its application root gate.
/// Call off the UI thread before starting a watcher or any other I/O beneath the requested root.
/// It rejects ambiguous/non-local syntax and reports an ancestor's reparse/offline attributes
/// without inspecting that ancestor's target. Drive-type policy remains the application's responsibility.
/// </summary>
public sealed class RecursiveDirectoryProbe : IRecursiveDirectoryProbe
{
    public FileAttributes GetAttributes(string path) => GetAttributes(path, CancellationToken.None);

    public FileAttributes GetAttributes(string path, CancellationToken cancellationToken)
    {
        RecursivePath.ValidateLocalRoot(path);
        // Inspect ancestors from the volume root outward, before touching the final target. A
        // normal-looking leaf beneath a junction can otherwise cross a volume or reach a network
        // target without carrying ReparsePoint on that leaf. Do not cache this mutable safety check.
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) throw new ArgumentException("An absolute directory path is required.", nameof(path));
        cancellationToken.ThrowIfCancellationRequested();
        var current = root;
        var attributes = File.GetAttributes(current);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) return attributes;
        foreach (var segment in path[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = Path.Combine(current, segment);
            attributes = File.GetAttributes(current);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) return attributes;
        }
        return attributes;
    }
}
