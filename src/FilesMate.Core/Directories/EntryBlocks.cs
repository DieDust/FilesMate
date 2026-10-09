using System.Collections;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Directories;

/// <summary>
/// Mutable rows owned exclusively by the EntryStore lock. A published block
/// becomes read-only; the next write clones only that block. Snapshot tables
/// own their array references and never retain an earlier snapshot object.
/// </summary>
internal sealed class EntryBlocks : IReadOnlyList<FileEntryCore>
{
    private readonly List<FileEntryCore[]> _blocks = [];
    private readonly List<bool> _shared = [];
    public int Count { get; private set; }
    internal int Capacity => _blocks.Count == 0 ? 0
        : (_blocks.Count - 1) * EntrySnapshot.BlockSize + _blocks[^1].Length;

    public FileEntryCore this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _blocks[index >> EntrySnapshot.BlockShift][index & (EntrySnapshot.BlockSize - 1)];
        }
        set
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            Writable(index >> EntrySnapshot.BlockShift)[index & (EntrySnapshot.BlockSize - 1)] = value;
        }
    }

    private FileEntryCore[] Writable(int block)
    {
        if (_shared[block])
        {
            _blocks[block] = (FileEntryCore[])_blocks[block].Clone();
            _shared[block] = false;
        }
        return _blocks[block];
    }

    public void Add(FileEntryCore entry, int appendRemaining = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(appendRemaining, 1);
        var block = Count >> EntrySnapshot.BlockShift;
        var offset = Count & (EntrySnapshot.BlockSize - 1);
        // The caller knows the remaining batch size. Allocate this block once
        // for those rows, keeping small folders small and published blocks sealed.
        var required = offset + Math.Min(appendRemaining, EntrySnapshot.BlockSize - offset);
        var capacity = 8;
        while (capacity < required) capacity *= 2;
        if (block == _blocks.Count)
        {
            _blocks.Add(new FileEntryCore[capacity]);
            _shared.Add(false);
        }
        else if (capacity > _blocks[block].Length)
        {
            var grown = new FileEntryCore[capacity];
            Array.Copy(_blocks[block], grown, offset);
            _blocks[block] = grown;
            _shared[block] = false;
        }
        Writable(block)[offset] = entry;
        Count++;
    }

    public void AddRange(IEnumerable<FileEntryCore> entries)
    {
        var remaining = entries is IReadOnlyCollection<FileEntryCore> collection ? collection.Count : 1;
        foreach (var entry in entries) Add(entry, Math.Max(1, remaining--));
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        for (var i = index; i < Count - 1; i++) this[i] = this[i + 1];
        var last = Count - 1;
        var block = last >> EntrySnapshot.BlockShift;
        if ((last & (EntrySnapshot.BlockSize - 1)) == 0)
        {
            _blocks.RemoveAt(block);
            _shared.RemoveAt(block);
        }
        else Writable(block)[last & (EntrySnapshot.BlockSize - 1)] = default;
        Count--;
    }

    public void Clear()
    {
        _blocks.Clear();
        _shared.Clear();
        Count = 0;
    }

    public FileEntryCore[] ToArray()
    {
        var result = new FileEntryCore[Count];
        var copied = 0;
        foreach (var block in _blocks)
        {
            var length = Math.Min(block.Length, Count - copied);
            Array.Copy(block, 0, result, copied, length);
            copied += length;
        }
        return result;
    }

    internal FileEntryCore[][] CaptureBlocks(CancellationToken cancellationToken)
    {
        var result = new FileEntryCore[_blocks.Count][];
        for (var i = 0; i < result.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[i] = _blocks[i];
            _shared[i] = true;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public IEnumerator<FileEntryCore> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
