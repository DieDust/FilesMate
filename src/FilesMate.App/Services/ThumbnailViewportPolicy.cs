namespace FilesMate.App.Services;

internal readonly record struct ThumbnailEntryRange(int First, int Last)
{
    public static ThumbnailEntryRange Empty => new(0, -1);
    public bool IsEmpty => Last < First;
    public int Count => IsEmpty ? 0 : Last - First + 1;
    public bool Contains(int index) => index >= First && index <= Last;

    // Grid reports rows; compact List reports columns. Both must be converted
    // to view-entry indices before demand reaches the loading layer.
    public static ThumbnailEntryRange FromBands(int first, int last, int entriesPerBand, int total)
    {
        if (first < 0 || last < first || entriesPerBand <= 0 || total <= 0) return Empty;
        var start = (long)first * entriesPerBand;
        if (start >= total) return Empty;
        return new((int)start, (int)Math.Min(total - 1L, ((long)last + 1) * entriesPerBand - 1));
    }
}

internal enum ThumbnailDemand
{
    None,
    RetainCompleted,
    Prefetch,
    DeferredVisible,
    Visible,
}

internal readonly record struct ThumbnailViewportDecision(
    long Epoch,
    ThumbnailEntryRange Visible,
    ThumbnailEntryRange Prefetch,
    ThumbnailEntryRange Retain,
    bool Fast,
    int Direction,
    double SpeedInViewportsPerSecond,
    long SettleAtMilliseconds,
    int MaxSpeculativeActive)
{
    public ThumbnailDemand DemandFor(int index, bool alreadyActive = false)
    {
        if (Visible.Contains(index)) return Fast && !alreadyActive ? ThumbnailDemand.DeferredVisible : ThumbnailDemand.Visible;
        if (Prefetch.Contains(index)) return ThumbnailDemand.Prefetch;
        if (Retain.Contains(index)) return ThumbnailDemand.RetainCompleted;
        return ThumbnailDemand.None;
    }
}

/// <summary>
/// Pure demand policy, deliberately independent of WinUI and decoder ownership.
/// It does not create tasks, hold images, release native slots or drop waiters.
/// Call Reset for navigation/layout/size epochs, Observe for geometry changes,
/// and Settle from one resettable timer even when wheel events report final.
/// </summary>
internal sealed class ThumbnailViewportPolicy
{
    internal const int DefaultSettleMilliseconds = 120;
    internal const int MaxPrefetchEntries = 16;
    internal const int MaxRetainedEntries = 4;
    internal const int RetentionMilliseconds = 250;
    internal const long RawBudgetBytes = 8L * 1024 * 1024;
    internal const long RetainSubBudgetBytes = 4L * 1024 * 1024;
    private const double EnterFastSpeed = 3;
    private const double LeaveFastSpeed = 1;
    private readonly int _settleMilliseconds;
    private bool _observed, _fast;
    private long _epoch, _lastMoveAt;
    private double _offset, _viewport, _speed;
    private int _direction = 1, _total, _pixels;
    private ThumbnailEntryRange _visible = ThumbnailEntryRange.Empty;

    public ThumbnailViewportPolicy(int settleMilliseconds = DefaultSettleMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settleMilliseconds);
        _settleMilliseconds = settleMilliseconds;
    }

    public void Reset()
    {
        _epoch++;
        _observed = _fast = false;
        _visible = ThumbnailEntryRange.Empty;
        _offset = _viewport = _speed = 0;
        _direction = 1;
        _total = _pixels = 0;
        _lastMoveAt = 0;
    }

    public ThumbnailViewportDecision Observe(long nowMilliseconds, double offset, double viewport,
        ThumbnailEntryRange visible, int total, int pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowMilliseconds);
        if (!double.IsFinite(offset) || !double.IsFinite(viewport) || viewport <= 0 || total <= 0)
        {
            Reset();
            return Decision();
        }
        _visible = visible.IsEmpty || visible.First >= total || visible.Last < 0 ? ThumbnailEntryRange.Empty
            : new(Math.Clamp(visible.First, 0, total - 1), Math.Clamp(visible.Last, 0, total - 1));
        _total = total;
        _pixels = pixels is > 0 and <= 2048 ? pixels : 0;
        _viewport = viewport;
        if (!_observed || nowMilliseconds < _lastMoveAt)
        {
            _observed = true;
            _fast = false;
            _speed = 0;
            _offset = offset;
            _lastMoveAt = nowMilliseconds;
        }
        else if (Math.Abs(offset - _offset) > .01)
        {
            var delta = offset - _offset;
            // Idle time is not evidence that the next one-frame jump is slow.
            var elapsed = Math.Clamp(nowMilliseconds - _lastMoveAt, 1, _settleMilliseconds);
            _direction = Math.Sign(delta);
            _speed = Math.Abs(delta) * 1000 / (_viewport * elapsed);
            _fast = _speed >= EnterFastSpeed || _fast && _speed > LeaveFastSpeed;
            _offset = offset;
            _lastMoveAt = nowMilliseconds;
        }
        else if (nowMilliseconds - _lastMoveAt >= _settleMilliseconds)
        {
            _fast = false;
            _speed = 0;
        }
        return Decision();
    }

    public ThumbnailViewportDecision Settle(long nowMilliseconds, long expectedEpoch)
    {
        if (expectedEpoch == _epoch && _observed && nowMilliseconds - _lastMoveAt >= _settleMilliseconds)
        {
            _fast = false;
            _speed = 0;
        }
        return Decision();
    }

    private ThumbnailViewportDecision Decision()
    {
        var prefetch = ThumbnailEntryRange.Empty;
        var retain = ThumbnailEntryRange.Empty;
        if (!_visible.IsEmpty && !_fast && _pixels > 0)
        {
            var squareBytes = 4L * _pixels * _pixels;
            var ahead = (int)Math.Min(Math.Min(MaxPrefetchEntries, (_visible.Count + 1L) / 2), RawBudgetBytes / Math.Max(1, squareBytes));
            // These are candidates, not strong leases. Admission uses each
            // already-decoded source's actual BGRA bytes in the shared cache.
            var behind = (int)Math.Min(MaxRetainedEntries, (_visible.Count + 3L) / 4);
            prefetch = Adjacent(ahead, _direction);
            retain = Adjacent(behind, -_direction);
        }
        return new(_epoch, _visible, prefetch, retain, _fast, _direction, _speed,
            _observed ? _lastMoveAt + _settleMilliseconds : 0, prefetch.IsEmpty ? 0 : 1);
    }

    private ThumbnailEntryRange Adjacent(int count, int direction)
    {
        if (count <= 0) return ThumbnailEntryRange.Empty;
        var first = direction > 0 ? (long)_visible.Last + 1 : Math.Max(0, _visible.First - count);
        var last = direction > 0 ? Math.Min(_total - 1L, _visible.Last + (long)count) : (long)_visible.First - 1;
        return first > last || first >= _total ? ThumbnailEntryRange.Empty : new((int)first, (int)last);
    }
}
