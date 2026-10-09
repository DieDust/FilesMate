using System.Collections;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Directories;

/// <summary>A store-owned, immutable set of rows. Indexes retain this exact data version.</summary>
public sealed class EntrySnapshot : IReadOnlyList<FileEntryCore>
{
    // Each block stays below the large-object threshold. A changed version owns a new
    // block table. Sealed blocks are shared with the store until its next write.
    internal const int BlockShift = 8;
    internal const int BlockSize = 1 << BlockShift;
    private readonly FileEntryCore[][] _blocks;

    private EntrySnapshot(EntryStore owner, long dataVersion, long mutationVersion, int count, FileEntryCore[][] blocks)
    {
        Owner = owner;
        DataVersion = dataVersion;
        MutationVersion = mutationVersion;
        Count = count;
        _blocks = blocks;
    }

    internal static EntrySnapshot Capture(EntryStore owner, long dataVersion, long mutationVersion,
        EntryBlocks entries,
        CancellationToken cancellationToken, out int copiedRows)
    {
        copiedRows = 0;
        var count = entries.Count;
        // Sharing the store's sealed rows removes the second directory-sized
        // struct copy. Writes clone their own affected blocks under the store lock.
        var blocks = entries.CaptureBlocks(cancellationToken);
        return new EntrySnapshot(owner, dataVersion, mutationVersion, count, blocks);
    }

    internal EntryStore Owner { get; }
    public long DataVersion { get; }
    private long MutationVersion { get; }
    public int Count { get; }
    public FileEntryCore this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new IndexOutOfRangeException();
            return _blocks[index >> BlockShift][index & (BlockSize - 1)];
        }
    }
    public bool BelongsTo(EntryStore store) => ReferenceEquals(Owner, store);
    public bool IsCurrent(EntryStore store) => BelongsTo(store) && DataVersion == store.DataVersion;

    /// <summary>
    /// True while this immutable prefix is unchanged in the live store. This does
    /// not mean the snapshot includes later appended rows or is a final listing.
    /// No store lock is taken on the UI publication path.
    /// </summary>
    public bool IsAppendCompatible(EntryStore store) => BelongsTo(store) && MutationVersion == store.MutationVersion;
    // A concrete enumerator keeps foreach over a snapshot allocation-free and avoids
    // copying the large row struct into a compiler-generated iterator's Current field.
    public Enumerator GetEnumerator() => new(this);
    IEnumerator<FileEntryCore> IEnumerable<FileEntryCore>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<FileEntryCore>
    {
        private readonly EntrySnapshot _snapshot;
        private int _index;
        internal Enumerator(EntrySnapshot snapshot) { _snapshot = snapshot; _index = -1; }
        public FileEntryCore Current => (uint)_index < (uint)_snapshot.Count
            ? _snapshot[_index] : throw new InvalidOperationException();
        object IEnumerator.Current => Current;
        public bool MoveNext()
        {
            if (_index < _snapshot.Count) _index++;
            return _index < _snapshot.Count;
        }
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}
