using System.Runtime.CompilerServices;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Recursive;

/// <summary>Admission accounting and overlap reserves. These fields are estimates, not measured process RAM.</summary>
public readonly record struct RecursiveMemoryEstimate(long NameStringsBytes, long NodeCapacityBytes,
    long StoreCapacityBytes, long SnapshotPublicationReserveBytes, long ProjectionPublicationReserveBytes,
    long CurrentDirectorySetsBytes, long FrontierCapacityBytes, long ObservationCacheReserveBytes,
    long ProviderAndPathReserveBytes, long ViewStateReserveBytes)
{
    public long TotalBytes => NameStringsBytes + NodeCapacityBytes + StoreCapacityBytes
        + SnapshotPublicationReserveBytes + ProjectionPublicationReserveBytes + CurrentDirectorySetsBytes
        + FrontierCapacityBytes + ObservationCacheReserveBytes + ProviderAndPathReserveBytes + ViewStateReserveBytes;

    internal static RecursiveMemoryEstimate Create(int count, long nameBytes, int nodeCapacity, int storeCapacity,
        int namesCapacity, int foldedCapacity, int frontierCapacity, long observationReserve, int rootLength,
        int batchSize, int maximumDepth, long viewBytes)
        => CreateForBatch(count, nameBytes, nodeCapacity, storeCapacity, namesCapacity, foldedCapacity,
            frontierCapacity, observationReserve, ProviderReserve(rootLength, batchSize, maximumDepth), 3 * viewBytes);

    internal static long ProviderReserve(int rootLength, int batchSize, int maximumDepth)
        => 4096 + 6L * batchSize * (Unsafe.SizeOf<FileEntryCore>() + 24 + 2 * 255)
            + 8L * (rootLength + (long)maximumDepth * 256);

    internal static RecursiveMemoryEstimate CreateForBatch(int count, long nameBytes, int nodeCapacity, int storeCapacity,
        int namesCapacity, int foldedCapacity, int frontierCapacity, long observationReserve,
        long providerReserve, long viewReserve)
    {
        var entrySize = Unsafe.SizeOf<FileEntryCore>();
        var nodeSize = Unsafe.SizeOf<RecursiveNode>();
        var blockCount = (count + 255L) / 256;
        // Lists can briefly hold the old and new arrays. Charge capacity, not Count.
        var nodes = 64 + (nodeCapacity + (long)nodeCapacity / 2) * nodeSize;
        var store = 64 + (storeCapacity + (long)storeCapacity / 2) * entrySize;
        // Current + worker/pending + prior publication can overlap. Block sharing only improves this reserve.
        var snapshots = 3 * (blockCount * (2L * IntPtr.Size + 256) + count * (long)(entrySize + nodeSize));
        // Structural/safety arrays, sort scratch, visible indexes and hash capacities;
        // reserve three publications plus pinyin key expansion (up to eight chars per source char).
        // Pinyin keys are temporary during one worker sort, not retained by every old projection.
        var projections = 3 * (128L * count + 256) + 8 * nameBytes;
        var sets = 128 + 32L * namesCapacity + 40L * foldedCapacity;
        var frontier = 64 + 6L * frontierCapacity;
        return new(nameBytes, nodes, store, snapshots, projections, sets, frontier,
            observationReserve, providerReserve, viewReserve);
    }

    internal static int ListCapacityFor(int allocated, int needed) => needed <= allocated ? allocated
        : (int)Math.Min(int.MaxValue, Math.Max(needed, allocated == 0 ? 4L : 2L * allocated));
    internal static int HashCapacityFor(int allocated, int needed) => needed <= allocated ? allocated
        : (int)Math.Min(int.MaxValue, 3L * needed + 7);
}

public readonly record struct RecursiveMemoryHeadroom(long AvailablePhysicalBytes, long RuntimeHeadroomBytes, long AddressSpaceHeadroomBytes);

/// <summary>One process-wide reservation pool. Retained completed and paused sessions keep their reservations until disposal.</summary>
public sealed class RecursiveMemoryBudgetPool(long maximumBytes, Func<RecursiveMemoryHeadroom> readHeadroom)
{
    public long MaximumBytes { get; } = maximumBytes > 0 ? maximumBytes : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
    private readonly Func<RecursiveMemoryHeadroom> _readHeadroom = readHeadroom ?? throw new ArgumentNullException(nameof(readHeadroom));
    private readonly object _gate = new();
    private readonly Dictionary<Guid, long> _reservations = [];
    private long _reserved, _capacity, _observedAt = long.MinValue;

    public (long ReservedBytes, long CapacityBytes, int Sessions) Statistics
    {
        get { lock (_gate) return (_reserved, _capacity, _reservations.Count); }
    }

    internal bool TryReserve(Guid session, long bytes)
    {
        lock (_gate)
        {
            var old = _reservations.GetValueOrDefault(session);
            if (bytes <= old) { _reserved += bytes - old; _reservations[session] = bytes; return true; }
            var now = Environment.TickCount64;
            if (_observedAt == long.MinValue || now - _observedAt >= 200)
            {
                var headroom = _readHeadroom();
                // Keep physical headroom for Windows and other apps; use at most one quarter of the remainder.
                _capacity = Math.Max(0, Math.Min(MaximumBytes, Math.Min(headroom.AvailablePhysicalBytes - 256L * 1024 * 1024,
                    Math.Min(headroom.RuntimeHeadroomBytes, headroom.AddressSpaceHeadroomBytes)) / 4));
                _observedAt = now;
            }
            if (bytes > _capacity - (_reserved - old)) return false;
            _reservations[session] = bytes;
            _reserved += bytes - old;
            return true;
        }
    }

    internal void Release(Guid session)
    {
        lock (_gate) if (_reservations.Remove(session, out var bytes)) _reserved -= bytes;
    }
}
