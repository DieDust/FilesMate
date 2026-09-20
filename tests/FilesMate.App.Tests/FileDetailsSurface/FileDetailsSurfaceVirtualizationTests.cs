using FilesMate.App.Controls.FileSurface;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.FileDetailsSurface;

public sealed class FileDetailsSurfaceVirtualizationTests
{
    [Fact]
    public void Reveal_finds_entries_in_filtered_sorted_views_using_one_index_mapping()
    {
        var store = new EntryStore();
        store.Append([File(1, "skip.txt"), File(2, "other.txt"), File(3, "target-z.txt"), File(4, "target-a.txt")]);
        var index = EntryViewIndex.Build(store, EntrySort.Name, new EntryFilter { Query = "target" }, NaturalStringComparer.Instance, 1);
        var source = new EntryItemsSource();
        source.TryPublish(store, index, 1);
        Assert.Equal(2, source.Count);
        Assert.True(source.TryFindEntry(entry => entry.Name == "target-a.txt", out var target));
        Assert.Equal(4, target.Id);
        var selection = new SelectionModel();
        selection.SelectOnly(target.Id);
        Assert.Equal(0, selection.ViewIndexOfPrimary(store, index));
        Assert.False(source.TryFindEntry(entry => entry.Name == "skip.txt", out _));
    }

    [Fact]
    public void One_hundred_thousand_entries_do_not_allocate_row_objects()
    {
        var (store, index) = Directory(100_000);
        var source = new EntryItemsSource();
        var changes = 0;
        source.CollectionChanged += (_, _) => changes++;

        Assert.True(source.TryPublish(store, index, currentGeneration: 1));
        Assert.Equal(100_000, source.Count);
        Assert.Equal(1, changes);
        Assert.True(source[0] is int);
        Assert.True(source.TryGetEntry(0, out var first));
        Assert.StartsWith("item", first.Name);
    }

    [Fact]
    public void Stale_generation_publish_is_ignored()
    {
        var (store, index) = Directory(10);
        var source = new EntryItemsSource();
        var changes = 0;
        source.CollectionChanged += (_, _) => changes++;

        Assert.False(source.TryPublish(store, index, currentGeneration: 2));
        Assert.Empty(source);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Batch_publish_emits_one_reset()
    {
        var (store, index) = Directory(64);
        var source = new EntryItemsSource();
        var changes = 0;
        source.CollectionChanged += (_, _) => changes++;
        source.TryPublish(store, index, 1);
        source.TryPublish(store, index, 1);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Repeated_clear_emits_only_one_reset()
    {
        var (store, index) = Directory(8);
        var source = new EntryItemsSource();
        var changes = 0;
        source.CollectionChanged += (_, _) => changes++;
        source.TryPublish(store, index, 1);

        source.ClearView();
        source.ClearView();

        Assert.Equal(2, changes);
        Assert.Empty(source);
        Assert.Equal(0, source.Generation);
    }

    [Fact]
    public void Visible_range_stays_bounded_for_a_100k_folder()
    {
        var tracker = new VisibleRangeTracker();
        var range = tracker.Update(verticalOffset: 0, viewportHeight: 640, itemHeight: FileColumnLayout.RowHeight, count: 100_000, overscanViewports: 1);
        Assert.True(range.RealizedCount < 80, $"realized {range.RealizedCount}");
        Assert.True(range.LastRealized < 80);
        Assert.Equal(28, FileColumnLayout.RowHeight);
    }

    [Fact]
    public void Selection_survives_reorder_by_id()
    {
        var store = new EntryStore();
        store.Append(
        [
            File(3, "c.txt"),
            File(1, "a.txt"),
            File(2, "b.txt"),
        ]);
        var unsorted = EntryViewIndex.Build(store, EntrySort.Size, EntryFilter.None, NaturalStringComparer.Instance, 1);
        var selection = new SelectionModel();
        selection.SelectOnly(2);
        var sorted = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.True(selection.Contains(2));
        Assert.Equal("b.txt", store[sorted[sorted.IndexOfId(store, 2)]].Name);
        Assert.Equal(unsorted.Count, sorted.Count);
    }

    [Fact]
    public void Recycled_row_content_replaces_name_and_selection()
    {
        var first = FileRowFormatter.Format(File(1, "old.txt"), selected: true);
        var second = FileRowFormatter.Format(Dir(2, "NewFolder"), selected: false);
        Assert.Equal("old.txt", first.Name);
        Assert.True(first.IsSelected);
        Assert.Equal("NewFolder", second.Name);
        Assert.False(second.IsSelected);
        Assert.True(second.IsDirectory);
        Assert.NotEqual(first.Type, second.Type);
    }

    [Fact]
    public void Range_select_uses_view_order()
    {
        var store = new EntryStore();
        store.Append([File(1, "a.txt"), File(2, "b.txt"), File(3, "c.txt")]);
        var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        var selection = new SelectionModel();
        selection.SelectRange(store, index, 0, 2);
        Assert.Equal(3, selection.Count);
        Assert.True(selection.Contains(1));
        Assert.True(selection.Contains(3));
    }

    private static (EntryStore Store, EntryViewIndex Index) Directory(int count)
    {
        var store = new EntryStore();
        var batch = new FileEntryCore[count];
        for (var i = 0; i < count; i++)
        {
            batch[i] = File(i + 1, $"item{i:D6}.txt");
        }

        store.Append(batch);
        return (store, EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1));
    }

    private static FileEntryCore File(int id, string name) =>
        new(id, name, 1, 1, 1, FileAttributes.Normal, EntryKind.File);

    private static FileEntryCore Dir(int id, string name) =>
        new(id, name, 0, 1, 1, FileAttributes.Directory, EntryKind.Directory);
}
