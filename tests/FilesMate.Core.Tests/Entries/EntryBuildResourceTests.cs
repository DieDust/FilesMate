using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Entries;

public sealed class EntryBuildResourceTests
{
    [Fact]
    public void Extension_filter_preserves_legacy_path_and_suffix_semantics()
    {
        string[] names = ["a.txt", "a.TXT", "txt", "reporttxt", ".txt", "a.", "archive.tar.gz", "a.txt.gz",
            "folder.txt/file", "folder.txt/file.txt", @"C:\folder.txt\file", @"C:\folder.txt\file.txt",
            "文档.文本", "文本", "a..txt", "a.txt ", "a", "folder/", "İ.TXT", "A.ｔｘｔ"];
        string[] queries = ["", "txt", ".txt", "TXT", ".TXT", ".", "gz", "tar.gz", ".tar.gz", "文本", "a", "txt ", "ｔｘｔ"];
        foreach (var name in names)
        foreach (var query in queries)
        foreach (var kind in new[] { EntryKind.File, EntryKind.Directory })
        {
            var extension = Path.GetExtension(name);
            var expected = query.Length == 0 || (query.StartsWith('.')
                ? extension.Equals(query, StringComparison.OrdinalIgnoreCase)
                : extension.Equals("." + query, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(query, StringComparison.OrdinalIgnoreCase));
            var filter = new EntryFilter { Query = query, Match = FilterMatchKind.Extension };
            Assert.Equal(expected, filter.Matches(Entry(name) with { Kind = kind }));
        }
    }

    [Theory]
    [InlineData("txt")]
    [InlineData(".txt")]
    public void Extension_filter_does_not_allocate_per_entry(string query)
    {
        var filter = new EntryFilter { Query = query, Match = FilterMatchKind.Extension };
        var entry = Entry("report.txt");
        for (var i = 0; i < 100; i++) filter.Matches(entry);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var matches = 0;
        for (var i = 0; i < 1_000; i++) if (filter.Matches(entry)) matches++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1_000, matches);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("", "#")]
    [InlineData("apple", "A")]
    [InlineData("Zebra", "Z")]
    [InlineData("9", "#")]
    [InlineData("文档", "其他")]
    [InlineData("éclair", "其他")]
    [InlineData("😀", "其他")]
    public void Initial_labels_keep_existing_meaning(string key, string expected) =>
        Assert.Equal(expected, PinyinName.Initial(key));

    [Fact]
    public void Latin_initials_reuse_the_same_labels()
    {
        for (var letter = 'a'; letter <= 'z'; letter++)
            Assert.Same(PinyinName.Initial(letter.ToString()), PinyinName.Initial(char.ToUpperInvariant(letter).ToString()));
    }

    [Theory]
    [InlineData("文档42_A😀.txt", "wendang42_A😀.txt")]
    [InlineData("报告10.中文", "baogao10.zhongwen")]
    public void Pinyin_keeps_non_Han_runes_and_replacement_characters(string name, string expected) =>
        Assert.Equal(expected, PinyinName.Key(name));

    [Fact]
    public void Pinyin_replaces_unpaired_surrogates_without_changing_rune_semantics()
    {
        // Construct at runtime: attribute metadata UTF-8 encoding cannot preserve invalid UTF-16.
        var name = new string(['文', (char)0xD800, '档', (char)0xDC00, '.', 't', 'x', 't']);
        Assert.Equal("wen�dang�.txt", PinyinName.Key(name));
    }

    [Fact]
    public void Precancelled_empty_build_is_cancelled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() => EntryViewIndex.Build(new EntryStore(),
            EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1, source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData(EntrySortColumn.Name)]
    [InlineData(EntrySortColumn.FullPath)]
    public void Cancellation_during_sort_stops_comparisons_and_preserves_token(EntrySortColumn column)
    {
        var store = LargeStore();
        using var source = new CancellationTokenSource();
        var calls = 0;
        var names = Comparer<string>.Create((x, y) =>
        {
            if (++calls == 1) source.Cancel();
            return NaturalStringComparer.Instance.Compare(x, y);
        });
        var error = Assert.Throws<OperationCanceledException>(() => EntryViewIndex.Build(store,
            EntrySort.Name with { Column = column }, EntryFilter.None, names, 1, source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.InRange(calls, 1, 128);
        // A cancelled build must return its temporary buffer and leave the store usable.
        store.Append([Entry("new.txt") with { Id = 99_999 }]);
        Assert.Equal(32_769, EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None,
            NaturalStringComparer.Instance, 2).Count);
    }

    [Fact]
    public void Genuine_comparer_failure_is_not_reclassified_as_cancellation()
    {
        var cause = new IOException("comparer failed");
        var names = Comparer<string>.Create((_, _) => throw cause);
        var error = Assert.Throws<InvalidOperationException>(() => EntryViewIndex.Build(LargeStore(),
            EntrySort.Name, EntryFilter.None, names, 1));
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void Unrelated_comparer_cancellation_is_not_unwrapped()
    {
        using var other = new CancellationTokenSource();
        other.Cancel();
        var cause = new OperationCanceledException(other.Token);
        var names = Comparer<string>.Create((_, _) => throw cause);
        var error = Assert.Throws<InvalidOperationException>(() => EntryViewIndex.Build(LargeStore(),
            EntrySort.Name, EntryFilter.None, names, 1));
        Assert.Same(cause, error.InnerException);
    }

    public static IEnumerable<object[]> SortContracts()
    {
        foreach (var column in Enum.GetValues<EntrySortColumn>())
        foreach (var grouping in Enum.GetValues<EntryGrouping>())
        foreach (var ascending in new[] { false, true })
        foreach (var mixedPinyin in new[] { false, true })
            yield return [column, grouping, ascending, mixedPinyin];
    }

    [Theory]
    [MemberData(nameof(SortContracts))]
    public void Sparse_matches_keep_source_indices_sort_order_and_section_boundaries(
        EntrySortColumn column, EntryGrouping grouping, bool ascending, bool mixedPinyin)
    {
        var store = new EntryStore();
        var entries = Enumerable.Range(1, 257).Select(i => new FileEntryCore(i,
            i % 3 == 0 ? $"文档{i % 7}.txt" : Path.Combine($"folder{i % 5}", $"file{i % 11}.dat"),
            (ulong)(i % 7), i % 9, i % 13, FileAttributes.Normal,
            i % 4 == 0 ? EntryKind.Directory : EntryKind.File) with { AccessedUtcTicks = i % 3 }).ToArray();
        new Random(20261001).Shuffle(entries);
        store.Append(entries);
        var properties = entries.Where(e => e.Id % 3 != 0).ToDictionary(e => e.Id,
            e => new EntryPropertyValue($"value{e.Id % 7}", e.Id % 7));
        var sort = EntrySort.Name with { Column = column, Grouping = grouping,
            Ascending = ascending, MixChineseAndLatin = mixedPinyin, PropertyName = "System.Title" };
        static ulong Size(FileEntryCore entry) => (ulong)(entry.Id % 17);
        static bool Matches(FileEntryCore entry) => entry.Id % 7 == 0;
        var full = EntryViewIndex.Build(store, sort, EntryFilter.None, NaturalStringComparer.Instance,
            1, null, sizeOf: Size, propertyValues: properties);
        var sparse = EntryViewIndex.Build(store, sort, EntryFilter.None, NaturalStringComparer.Instance,
            2, Matches, sizeOf: Size, propertyValues: properties);
        Assert.Equal(full.Where(row => Matches(entries[row])), sparse);
        Assert.All(sparse, row => Assert.True(Matches(entries[row])));
        Assert.Equal(2, sparse.Generation);
        if (column != EntrySortColumn.Name) Assert.Empty(sparse.NameSections);
        else
        {
            var sections = new List<NameSection>();
            for (var i = 0; i < sparse.Count; i++)
            {
                var entry = entries[sparse[i]];
                var label = PinyinName.Initial(PinyinName.Key(entry.Name));
                var directory = grouping != EntryGrouping.Mixed && entry.Kind == EntryKind.Directory;
                if (sections.Count == 0 || sections[^1].Label != label || sections[^1].IsDirectory != directory)
                    sections.Add(new(label, i, directory));
            }
            Assert.Equal(sections, sparse.NameSections);
        }
    }

    [Fact]
    public void Sparse_size_keys_are_sampled_once_only_for_matching_rows()
    {
        var store = LargeStore();
        var reads = new Dictionary<int, int>();
        var index = EntryViewIndex.Build(store, EntrySort.Size, EntryFilter.None, NaturalStringComparer.Instance,
            1, entry => entry.Id % 1_000 == 0, sizeOf: entry =>
            {
                reads[entry.Id] = reads.GetValueOrDefault(entry.Id) + 1;
                return (ulong)entry.Id;
            });
        Assert.Equal(32, index.Count);
        Assert.All(reads.Values, count => Assert.Equal(1, count));
        Assert.Equal(Enumerable.Range(1, 32).Select(i => i * 1_000), index.Select(i => store[i].Id));
    }

    [Fact]
    public void Zero_and_sparse_matches_do_not_allocate_directory_sized_keys()
    {
        var store = LargeStore();
        foreach (var query in new[] { "missing", "item000" })
        {
            var filter = new EntryFilter { Query = query, Match = FilterMatchKind.Prefix };
            for (var i = 0; i < 3; i++)
                EntryViewIndex.Build(store, EntrySort.Name, filter, NaturalStringComparer.Instance, 1);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var index = EntryViewIndex.Build(store, EntrySort.Name, filter, NaturalStringComparer.Instance, 1);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.InRange(index.Count, 0, 999);
            Assert.True(allocated < 32_768, $"Sparse name build allocated {allocated} bytes.");
        }
    }

    private static EntryStore LargeStore()
    {
        var store = new EntryStore();
        store.Append(Enumerable.Range(0, 32_768).Select(i => Entry($"item{32_768 - i:D6}.txt") with { Id = i + 1 }).ToArray());
        return store;
    }

    private static FileEntryCore Entry(string name) => new(1, name, 1, 1, 1, FileAttributes.Normal, EntryKind.File);
}
