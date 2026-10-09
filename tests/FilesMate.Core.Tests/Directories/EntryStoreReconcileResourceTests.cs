using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Directories;

public sealed class EntryStoreReconcileResourceTests
{
    [Fact]
    public void Reconcile_keeps_legacy_identity_and_change_sets_across_mixed_updates()
    {
        var expected = Enumerable.Range(1, 1_000).Select(i => Entry(i * 10, $"item{i}.txt", (ulong)i)).ToList();
        var store = new EntryStore();
        store.Append(expected);
        var nextId = 10_001;
        var random = new Random(20261001);
        for (var round = 0; round < 200; round++)
        {
            var listing = expected.Select(entry => entry with { Id = 0 }).ToList();
            if (round % 11 == 0) listing.Reverse();
            if (round % 7 == 0 && listing.Count > 0) listing.RemoveAt(random.Next(listing.Count));
            if (round % 3 == 0) listing.Add(Entry(0, $"added{round}.txt", (ulong)round));
            if (round % 5 == 0 && listing.Count > 0)
            {
                var row = random.Next(listing.Count);
                listing[row] = listing[row] with { Size = listing[row].Size + 1, AccessedUtcTicks = round };
            }
            if (round % 13 == 0 && listing.Count > 0)
                listing[0] = listing[0] with { Name = listing[0].Name.ToUpperInvariant() };
            if (round > 0 && round % 53 == 0) listing.Clear();
            var expectedChanged = LegacyReconcile(expected, ref nextId, listing);
            var actualChanged = store.Reconcile(listing);
            Assert.Equal(expectedChanged.Order(StringComparer.Ordinal), actualChanged.Order(StringComparer.Ordinal));
            Assert.Equal(expected, store.Snapshot());
        }
    }

    [Fact]
    public void Duplicate_existing_names_keep_the_same_exception_and_do_not_mutate_the_store()
    {
        var entries = new[] { Entry(10, "a.txt"), Entry(20, "A.TXT") };
        var store = new EntryStore();
        store.Append(entries);
        Assert.Throws<ArgumentException>(() => store.Reconcile(entries));
        Assert.Equal(entries, store.Snapshot());
    }

    [Fact]
    public void Duplicate_incoming_names_keep_legacy_new_identity_behavior()
    {
        var store = new EntryStore();
        store.Append([Entry(20, "a.txt")]);
        Assert.Equal(["A.TXT"], store.Reconcile([Entry(0, "a.txt"), Entry(0, "A.TXT", 9)]));
        Assert.Equal([20, 21], store.Snapshot().Select(entry => entry.Id));
        Assert.Throws<ArgumentException>(() => store.Reconcile(store.Snapshot()));
    }

    [Fact]
    public void Reconcile_lookup_does_not_duplicate_all_entry_values()
    {
        var listing = Enumerable.Range(1, 10_000).Select(i => Entry(i, $"item{i}.txt")).ToArray();
        var store = new EntryStore();
        store.Append(listing);
        for (var i = 0; i < 3; i++) store.Reconcile(listing);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var changed = store.Reconcile(listing);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(changed);
        Assert.True(allocated < 1_000_000, $"10K reconcile allocated {allocated} bytes.");
    }

    // Frozen pre-optimization algorithm: an independent oracle for identities, incoming order,
    // duplicate handling and changed-path semantics, not a timing reference.
    private static IReadOnlyList<string> LegacyReconcile(List<FileEntryCore> entries, ref int nextId,
        IReadOnlyList<FileEntryCore> listing)
    {
        var previous = entries.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        var next = new List<FileEntryCore>(listing.Count);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in listing)
        {
            if (previous.Remove(incoming.Name, out var existing))
            {
                var replacement = incoming with { Id = existing.Id };
                if (replacement != existing) changed.Add(incoming.Name);
                next.Add(replacement);
            }
            else
            {
                next.Add(incoming with { Id = nextId++ });
                changed.Add(incoming.Name);
            }
        }
        foreach (var removed in previous.Keys) changed.Add(removed);
        if (changed.Count == 0) return [];
        entries.Clear();
        entries.AddRange(next);
        return changed.ToArray();
    }

    private static FileEntryCore Entry(int id, string name, ulong size = 1) =>
        new(id, name, size, 1, 1, FileAttributes.Normal, EntryKind.File);
}
