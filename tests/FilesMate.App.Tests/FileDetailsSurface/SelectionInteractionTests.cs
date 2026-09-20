using FilesMate.App.Controls.FileSurface;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.FileDetailsSurface;

public sealed class SelectionInteractionTests
{
    [Fact]
    public void Repeated_upward_extension_advances_the_active_end()
    {
        var (store, index) = Directory();
        var selection = new SelectionModel();
        selection.SelectOnly(5);
        for (var target = 3; target >= 1; target--)
        {
            var current = selection.ViewIndexOfPrimary(store, index);
            selection.SelectRange(store, index, 4, current - 1);
            Assert.Equal(target, selection.ViewIndexOfPrimary(store, index));
            Assert.Equal(5 - target, selection.Count);
            Assert.Equal(5, selection.AnchorId);
        }
    }

    [Fact]
    public void Reversing_range_direction_shrinks_towards_the_original_anchor()
    {
        var (store, index) = Directory();
        var selection = new SelectionModel();
        selection.SelectOnly(5);
        selection.SelectRange(store, index, 4, 1);
        selection.SelectRange(store, index, 4, selection.ViewIndexOfPrimary(store, index) + 1);
        Assert.Equal(new[] { 3, 4, 5 }, selection.Ids.Order());
        Assert.Equal(3, selection.PrimaryId);
        Assert.Equal(5, selection.AnchorId);
    }

    [Fact]
    public void Selecting_an_empty_view_clears_primary_and_anchor()
    {
        var (store, _) = Directory();
        var empty = EntryViewIndex.Build(store, EntrySort.Name, new EntryFilter { Query = "missing" }, NaturalStringComparer.Instance, 1);
        var selection = new SelectionModel();
        selection.SelectOnly(3);
        selection.SelectAll(store, empty);
        Assert.Empty(selection.Ids);
        Assert.Null(selection.PrimaryId);
        Assert.Null(selection.AnchorId);
    }

    [Fact]
    public void External_deletion_removes_only_missing_selection_and_repairs_anchor()
    {
        var (store, _) = Directory();
        var selection = new SelectionModel();
        selection.SelectOnly(3);
        selection.Toggle(4);
        store.RemoveByName("file3.txt");
        Assert.True(selection.RemoveMissing(store));
        Assert.Equal(new[] { 4 }, selection.Ids);
        Assert.Equal(4, selection.PrimaryId);
        Assert.Equal(4, selection.AnchorId);
        Assert.False(selection.RemoveMissing(store));
        store.RemoveByName("file4.txt");
        Assert.True(selection.RemoveMissing(store));
        Assert.Empty(selection.Ids);
        Assert.Null(selection.PrimaryId);
        Assert.Null(selection.AnchorId);
    }

    [Fact]
    public void External_deletion_repairs_deselected_keyboard_focus()
    {
        var (store, _) = Directory();
        var selection = new SelectionModel();
        selection.SelectOnly(3);
        selection.Toggle(4);
        selection.Toggle(4);
        store.RemoveByName("file4.txt");
        Assert.True(selection.RemoveMissing(store));
        Assert.Equal(3, selection.PrimaryId);
        Assert.Equal(3, selection.AnchorId);
    }

    private static (EntryStore, EntryViewIndex) Directory()
    {
        var store = new EntryStore();
        store.Append(Enumerable.Range(1, 6).Select(i => new FileEntryCore(i, $"file{i}.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File)).ToArray());
        return (store, EntryViewIndex.Build(store, EntrySort.Name, new EntryFilter(), NaturalStringComparer.Instance, 1));
    }
}
