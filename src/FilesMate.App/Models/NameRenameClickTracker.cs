namespace FilesMate.App.Models;

// A row/tile can be recycled while a click is pending. Retain data identity only.
internal readonly record struct NameRenameTarget(long Generation, int EntryId, string Path);

internal readonly record struct NameRenameOpeningWindow(NameRenameTarget Target, long Revision, long Until);

internal sealed class NameRenameClickTracker
{
    private Click? _previous;
    private Pending? _pending;
    internal bool HasPending => _pending is not null;
    internal NameRenameOpeningWindow? PendingOpeningWindow => _pending is { } pending
        ? new(pending.Click.Target, pending.Click.Revision, pending.DoubleClickUntil) : null;
    internal static bool CanCompleteOpening(NameRenameOpeningWindow window, NameRenameTarget current,
        long revision, long now, bool unchanged) => unchanged && now <= window.Until
        && SameTarget(window.Target, current) && window.Revision == revision;
    internal long RemainingDelay(long now) => _pending is { } pending ? Math.Max(1, pending.Due - now) : 0;
    internal bool TracksEntry(int id) => _previous?.Target.EntryId == id || _pending?.Click.Target.EntryId == id;

    internal static bool SameTarget(NameRenameTarget left, NameRenameTarget right) =>
        left.Generation == right.Generation && left.EntryId == right.EntryId
        && string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);

    internal void Cancel(bool retainPrevious = false)
    {
        _pending = null;
        if (!retainPrevious) _previous = null;
    }

    internal void ValidateSelection(long revision)
    {
        if (_previous is { } previous && previous.Revision != revision) Cancel();
    }

    // Called only after a completed, stable, unmodified click and the opening
    // decision. The first click may select the item but cannot immediately rename.
    internal bool CompleteClick(NameRenameTarget target, long revision, bool wasOnlySelected,
        ItemOpeningMode mode, bool onName, bool opened, long now, int doubleClickMilliseconds)
    {
        _pending = null;
        if (opened || mode != ItemOpeningMode.DoubleClick || string.IsNullOrEmpty(target.Path))
        {
            Cancel();
            return false;
        }
        var interval = Math.Max(1, doubleClickMilliseconds);
        var click = new Click(target, revision, now);
        if (onName && wasOnlySelected && _previous is { } previous
            && SameTarget(previous.Target, target) && previous.Revision == revision
            && now >= previous.Milliseconds && now - previous.Milliseconds > interval)
        {
            // Show the editor promptly. Until the full system interval elapses,
            // an unchanged editor still routes a genuine next click through the
            // same opening tracker; editing or ownership changes retire that window.
            _pending = new(click, now + Math.Min(interval, 160) + 1L, now + interval);
        }
        _previous = click;
        return HasPending;
    }

    internal bool TryConsume(NameRenameTarget current, long revision, bool eligible, long now,
        out NameRenameTarget target)
    {
        target = default;
        if (_pending is not { } pending) return false;
        if (!eligible || !SameTarget(pending.Click.Target, current) || pending.Click.Revision != revision)
        {
            Cancel();
            return false;
        }
        if (now < pending.Due) return false;
        target = pending.Click.Target;
        Cancel();
        return true;
    }

    private readonly record struct Click(NameRenameTarget Target, long Revision, long Milliseconds);
    private readonly record struct Pending(Click Click, long Due, long DoubleClickUntil);
}
