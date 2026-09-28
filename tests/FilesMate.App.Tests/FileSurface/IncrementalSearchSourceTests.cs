using System.Collections.Specialized;
using FilesMate.App.Controls.FileSurface;
using FilesMate.Core.Entries;
using FilesMate.Core.Directories;

namespace FilesMate.App.Tests.FileSurface;

public sealed class IncrementalSearchSourceTests
{
    [Fact]
    public void Appending_results_notifies_only_the_added_range_and_keeps_ids_stable()
    {
        var store = new EntryStore();
        FileEntryCore Row(int id) => new(id, $"file-{id}.txt", 1, 0, 0, FileAttributes.Normal, EntryKind.File);
        store.Append(Enumerable.Range(0, 200).Select(Row).ToArray());
        var source = new EntryItemsSource();
        source.TryPublish(store, EntryViewIndex.InSourceOrder(store, 1), 1);
        NotifyCollectionChangedEventArgs? change = null;
        source.CollectionChanged += (_, e) => change = e;
        store.Append(Enumerable.Range(200, 200).Select(Row).ToArray());
        source.TryPublish(store, EntryViewIndex.InSourceOrder(store, 1), 1, append: true);
        Assert.Equal(NotifyCollectionChangedAction.Add, change!.Action);
        Assert.Equal(200, change.NewStartingIndex); Assert.Equal(200, change.NewItems!.Count);
        Assert.Equal(400, source.Count); Assert.True(source.TryGetEntry(17, out var entry)); Assert.Equal(17, entry.Id);
        source.ClearView(); Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
    }
}
