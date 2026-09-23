using System.IO;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Entries;

public sealed class EntryViewIndexTests
{
    [Fact]
    public void AccessDateSortUsesTimestampInsteadOfFilename()
    {
        var store = Store(File("a.txt") with { AccessedUtcTicks = 300 }, File("b.txt") with { AccessedUtcTicks = 100 });
        var index = EntryViewIndex.Build(store, EntrySort.Name with { Column = EntrySortColumn.Accessed }, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Equal(["b.txt", "a.txt"], Names(store, index));
    }

    [Fact]
    public void LocationSortGroupsTaggedItemsByParentPath()
    {
        var store = Store(File(@"D:\B\a.txt"), File(@"D:\A\z.txt"));
        var index = EntryViewIndex.Build(store, EntrySort.Name with { Column = EntrySortColumn.Location }, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Equal([@"D:\A\z.txt", @"D:\B\a.txt"], Names(store, index));
    }

    [Fact]
    public void Mixed_pinyin_sort_is_opt_in_and_preserves_natural_numbers()
    {
        var store = Store(File("阿3.txt"), File("A10.txt"), File("A2.txt"), File("中.txt"), File("B.txt"));
        var separate = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Equal(["A2.txt", "A10.txt", "B.txt", "阿3.txt", "中.txt"], Names(store, separate));
        var mixed = EntryViewIndex.Build(store, EntrySort.Name with { MixChineseAndLatin = true }, EntryFilter.None, NaturalStringComparer.Instance, 2);
        Assert.Equal(["A2.txt", "阿3.txt", "A10.txt", "B.txt", "中.txt"], Names(store, mixed));
        Assert.Equal(["A", "B", "Z"], mixed.NameSections.Select(s => s.Label));
        Assert.Equal(["A", "B", "A", "Z"], separate.NameSections.Select(s => s.Label));
    }

    [Fact]
    public void Alphabet_destinations_follow_actual_folder_first_descending_filtered_order()
    {
        var store = Store(File("A1.txt"), File("Z1.txt"), Dir("A2"), Dir("Z2"), File("B-hidden.txt", hidden: true));
        var sort = EntrySort.Name with { Ascending = false };
        var index = EntryViewIndex.Build(store, sort, EntryFilter.None, NaturalStringComparer.Instance, 1,
            additionalMatch: entry => !entry.Name.Contains("hidden"));
        Assert.Equal(["Z2", "A2", "Z1.txt", "A1.txt"], Names(store, index));
        Assert.Equal([0, 1, 2, 3], index.NameSections.Select(s => s.FirstIndex));
        Assert.Equal([true, true, false, false], index.NameSections.Select(s => s.IsDirectory));
        Assert.Equal(["Z", "A", "Z", "A"], index.NameSections.Select(s => s.Label));
        Assert.Empty(EntryViewIndex.Build(store, EntrySort.Size, EntryFilter.None, NaturalStringComparer.Instance, 1).NameSections);
    }

    [Fact]
    public void Alphabet_can_reach_the_same_initial_in_folder_and_file_groups()
    {
        var store = Store(Dir("Alpha"), Dir("Beta"), File("Alpha.txt"), File("Beta.txt"));
        var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        var navigation = new AlphabetNavigation(index.NameSections, index.Count);

        Assert.Equal(["Alpha", "Beta", "Alpha.txt", "Beta.txt"], Names(store, index));
        Assert.Equal(["A", "B", "A", "B"], index.NameSections.Select(section => section.Label));
        Assert.True(navigation.HasBothKinds("A"));
        Assert.Equal(0, navigation.Destination("A", row: 2, isDirectory: true));
        Assert.Equal(2, navigation.Destination("A", row: 0, isDirectory: false));
    }

    [Fact]
    public void Separate_latin_and_pinyin_folder_runs_are_one_kind()
    {
        var store = Store(Dir("Forest"), Dir("Gallery"), Dir("方便"));
        var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        var navigation = new AlphabetNavigation(index.NameSections, index.Count);

        Assert.Equal(["F", "G", "F"], index.NameSections.Select(section => section.Label));
        Assert.Equal([true, true, true], index.NameSections.Select(section => section.IsDirectory));
        Assert.False(navigation.HasBothKinds("F"));
        Assert.Equal(0, navigation.Destination("F", row: 0));
        Assert.Equal(2, navigation.Destination("F", row: 2));
    }

    [Theory]
    [InlineData("文档10.md", "wendang10.md")]
    [InlineData("繁體.txt", "fanti.txt")]
    [InlineData("绿.txt", "lv.txt")]
    [InlineData("report2.txt", "report2.txt")]
    public void Offline_pinyin_keeps_numbers_and_extensions(string name, string expected) => Assert.Equal(expected, PinyinName.Key(name));

    [Fact]
    public void Natural_name_order_puts_file2_before_file10()
    {
        var store = Store(
            File("file10.txt"),
            File("file2.txt"),
            File("file1.txt"));

        var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, generation: 1);
        Assert.Equal(["file1.txt", "file2.txt", "file10.txt"], Names(store, index));
    }

    [Fact]
    public void Directories_can_sort_first()
    {
        var store = Store(
            File("a.txt"),
            Dir("z-folder"),
            File("m.txt"));

        var index = EntryViewIndex.Build(store, EntrySort.Name with { DirectoriesFirst = true }, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Equal(["z-folder", "a.txt", "m.txt"], Names(store, index));
    }

    [Fact]
    public void Name_sort_is_case_insensitive_and_preserves_display_name()
    {
        var store = Store(File("B.txt"), File("a.txt"));
        var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Equal(["a.txt", "B.txt"], Names(store, index));
        Assert.Equal("B.txt", store[index[1]].Name);
    }

    [Fact]
    public void Equal_keys_keep_stable_original_order()
    {
        var store = Store(
            File("same.txt", size: 10, id: 1),
            File("same.txt", size: 10, id: 2),
            File("same.txt", size: 10, id: 3));

        var index = EntryViewIndex.Build(store, EntrySort.Size, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Equal([1, 2, 3], index.Select(i => store[i].Id).ToArray());
    }

    [Fact]
    public void Size_sort_can_use_cached_folder_sizes()
    {
        var store = Store(
            Dir("small"),
            Dir("large"),
            File("a.bin", size: 50));
        var sizes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["small"] = 1,
            ["large"] = 100,
        };

        var index = EntryViewIndex.Build(
            store,
            EntrySort.Size with { Ascending = false, DirectoriesFirst = true },
            EntryFilter.None,
            NaturalStringComparer.Instance,
            generation: 1,
            additionalMatch: null,
            sizeOf: entry => entry.Kind == EntryKind.Directory && sizes.TryGetValue(entry.Name, out var size)
                ? size
                : entry.Size);

        Assert.Equal(["large", "small", "a.bin"], Names(store, index));
    }

    [Fact]
    public void Missing_created_times_sort_after_known_values_when_ascending()
    {
        var store = Store(
            File("unknown.txt", created: 0),
            File("known.txt", created: 100));

        var index = EntryViewIndex.Build(store, EntrySort.Created, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Equal(["known.txt", "unknown.txt"], Names(store, index));
    }

    [Fact]
    public void Filter_supports_substring_prefix_extension_and_kind()
    {
        var store = Store(
            File("readme.txt"),
            File("Readme.md"),
            Dir("src"),
            File("hidden.log", hidden: true));

        Assert.Equal(["Readme.md", "readme.txt"], Names(store, Filter(store, new EntryFilter { Query = "read", Match = FilterMatchKind.Substring })));
        Assert.Equal(["readme.txt"], Names(store, Filter(store, new EntryFilter { Query = "readme.t", Match = FilterMatchKind.Prefix })));
        Assert.Equal(["readme.txt"], Names(store, Filter(store, new EntryFilter { Query = ".txt", Match = FilterMatchKind.Extension })));
        Assert.Equal(["src"], Names(store, Filter(store, new EntryFilter { DirectoriesOnly = true })));
        Assert.Equal(["Readme.md", "readme.txt"], Names(store, Filter(store, new EntryFilter { FilesOnly = true, IncludeHidden = false })));
    }

    [Fact]
    public void Clearing_a_filter_is_never_debounced()
    {
        Assert.False(EntryViewIndex.ShouldDebounce(100_000, "abc", string.Empty));
        Assert.False(EntryViewIndex.ShouldDebounce(100, "abc", "abcd"));
        Assert.True(EntryViewIndex.ShouldDebounce(100_000, "ab", "abc"));
    }

    [Fact]
    public void Stale_query_results_are_rejected()
    {
        var store = Store(File("alpha.txt"), File("beta.txt"));
        var stale = EntryViewIndex.Build(store, EntrySort.Name, new EntryFilter { Query = "al" }, NaturalStringComparer.Instance, generation: 1);
        Assert.False(stale.Matches(generation: 2, new EntryFilter { Query = "be" }));
        Assert.True(stale.Matches(generation: 1, new EntryFilter { Query = "al" }));
    }

    [Fact]
    public void Selection_survives_reorder_by_stable_id()
    {
        var store = Store(File("c.txt", id: 3), File("a.txt", id: 1), File("b.txt", id: 2));
        var selected = new HashSet<int> { 2 };
        var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        var stillSelected = index.Select(i => store[i].Id).Where(selected.Contains).ToArray();
        Assert.Equal([2], stillSelected);
        var viewIndex = index.IndexOfId(store, 2);
        Assert.True(viewIndex >= 0);
        Assert.Equal("b.txt", store[index[viewIndex]].Name);
    }

    [Fact]
    public void Filter_100k_names_completes_quickly()
    {
        var store = new EntryStore();
        var batch = new FileEntryCore[100_000];
        for (var i = 0; i < batch.Length; i++)
        {
            batch[i] = File($"item{i:D6}.txt", id: i + 1);
        }

        store.Append(batch);
        var filter = new EntryFilter { Query = "item000", Match = FilterMatchKind.Prefix };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var index = EntryViewIndex.Build(store, EntrySort.Name, filter, NaturalStringComparer.Instance, 1);
        sw.Stop();
        Assert.True(index.Count > 0);
        Assert.True(sw.ElapsedMilliseconds < 100, $"100k prefix filter took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Additional_in_memory_match_rebuilds_only_the_index()
    {
        var store = Store(
            File("alpha.txt", id: 1),
            File("beta.txt", id: 2),
            File("gamma.txt", id: 3));

        var index = EntryViewIndex.Build(
            store,
            EntrySort.Name,
            EntryFilter.None,
            NaturalStringComparer.Instance,
            generation: 7,
            additionalMatch: entry => entry.Id != 2);

        Assert.Equal(["alpha.txt", "gamma.txt"], Names(store, index));
        Assert.Equal(7, index.Generation);
    }

    private static EntryViewIndex Filter(EntryStore store, EntryFilter filter) =>
        EntryViewIndex.Build(store, EntrySort.Name, filter, NaturalStringComparer.Instance, 1);

    private static string[] Names(EntryStore store, EntryViewIndex index) =>
        index.Select(i => store[i].Name).ToArray();

    private static EntryStore Store(params FileEntryCore[] entries)
    {
        var store = new EntryStore();
        store.Append(entries);
        return store;
    }

    private static FileEntryCore File(string name, int id = 0, ulong size = 1, long created = 1, bool hidden = false) =>
        new(
            id == 0 ? PositiveId(name) : id,
            name,
            size,
            ModifiedUtcTicks: 1,
            CreatedUtcTicks: created,
            hidden ? FileAttributes.Hidden : FileAttributes.Normal,
            EntryKind.File);

    private static FileEntryCore Dir(string name) =>
        new(PositiveId(name), name, 0, 1, 1, FileAttributes.Directory, EntryKind.Directory);

    private static int PositiveId(string name)
    {
        var hash = name.GetHashCode(StringComparison.Ordinal);
        return hash == int.MinValue ? 1 : Math.Abs(hash);
    }
}
