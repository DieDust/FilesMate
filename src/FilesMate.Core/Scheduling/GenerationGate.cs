using FilesMate.Core.Navigation;

namespace FilesMate.Core.Scheduling;

/// <summary>
/// Pane-scoped generation counter. Only the latest generation may publish into UI-facing state.
/// Thread-safety: increment and checks are atomic. Ownership: one gate per pane, owned by the navigation controller.
/// Cancellation: advancing the generation does not cancel work; the previous session must be disposed.
/// Staleness: a matching generation may still describe a filesystem that has since changed.
/// </summary>
public sealed class GenerationGate
{
    private long _current;

    public GenerationGate(PaneId paneId)
    {
        PaneId = paneId;
    }

    public PaneId PaneId { get; }

    public long CurrentGeneration => Volatile.Read(ref _current);

    public long Begin() => Interlocked.Increment(ref _current);

    public bool Allows(PaneId paneId, long generation) =>
        paneId == PaneId && generation == Volatile.Read(ref _current);
}
