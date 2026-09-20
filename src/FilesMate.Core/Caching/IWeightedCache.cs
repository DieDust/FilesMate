namespace FilesMate.Core.Caching;

/// <summary>
/// Bounded cache keyed by value identity. Weights are retained decoded/managed bytes, not source file sizes.
/// Thread-safety: implementations must document their concurrency model; UI code must not block on cache fills.
/// Ownership: inserting a disposable value transfers disposal responsibility to the cache on eviction.
/// Cancellation: in-flight loads are coalesced by key; cancelled waiters must not publish into the cache.
/// </summary>
public interface IWeightedCache<TKey, TValue>
{
    public long CurrentBytes { get; }

    public long MaxBytes { get; }

    public bool TryGet(TKey key, out TValue? value);

    public bool Set(TKey key, TValue value, long weight);
}

/// <summary>
/// Running byte budget used by weighted caches. Not thread-safe; the owning cache serializes access.
/// </summary>
public sealed class CacheBudget
{
    public CacheBudget(long maxBytes)
    {
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        MaxBytes = maxBytes;
    }

    public long CurrentBytes { get; private set; }

    public long MaxBytes { get; }

    public void Add(long weight)
    {
        if (weight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight));
        }

        CurrentBytes += weight;
    }

    public bool TryReserve(long weight)
    {
        if (weight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight));
        }

        if (CurrentBytes + weight > MaxBytes)
        {
            return false;
        }

        CurrentBytes += weight;
        return true;
    }

    public void Release(long weight)
    {
        if (weight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight));
        }

        if (weight > CurrentBytes)
        {
            throw new InvalidOperationException("Cannot release more bytes than the budget holds.");
        }

        CurrentBytes -= weight;
    }
}
