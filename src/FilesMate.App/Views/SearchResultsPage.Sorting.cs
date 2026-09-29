using System.Collections.Concurrent;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Sorting;

namespace FilesMate.App.Views;

public sealed partial class SearchResultsPage
{
    private EntrySort? _resultSort;
    private CancellationTokenSource? _resultSortBuild;
    private void SetResultSort(EntrySort sort)
    {
        sort = sort with { MixChineseAndLatin = App.ExplorerPreferences.MixChineseAndLatin };
        _resultSort = sort;
        Results.SetSort(sort);
        Commands.SetSort(sort);
        UpdateStatusSelection();
        _ = ApplyResultSortAsync();
    }
    private async Task ApplyResultSortAsync()
    {
        _resultSortBuild?.Cancel();
        if (_resultSort is not { } sort) return;
        using var cancellation = new CancellationTokenSource();
        _resultSortBuild = cancellation;
        var generation = _generation; var store = _resultStore; var rows = _rows;
        try
        {
            // Name/size/date/type already exist in the result snapshot. Use the same
            // comparer as folder views, including pinyin and alphabet sections.
            var property = sort.Column switch
            {
                EntrySortColumn.ShellProperty => sort.PropertyName,
                EntrySortColumn.Created => "System.DateCreated", EntrySortColumn.Accessed => "System.DateAccessed",
                EntrySortColumn.Attributes => "System.FileAttributes", _ => null
            };
            ConcurrentDictionary<int, EntryPropertyValue>? values = null;
            var indexSort = sort;
            if (DetailsColumn.IsPropertyName(property) || sort.Column is EntrySortColumn.Location or EntrySortColumn.FullPath)
            {
                values = new();
                indexSort = sort with { Column = EntrySortColumn.ShellProperty };
                var entries = store.Observe(items => items.ToArray());
                await Parallel.ForEachAsync(entries, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellation.Token }, async (entry, token) =>
                {
                    var row = rows[entry.Id];
                    if (property is not null)
                    {
                        var metadata = await FilePropertyCache.GetAsync(row.Path, entry, [property], token).ConfigureAwait(false);
                        if (metadata.TryGetValue(property, out var value)) values[entry.Id] = value;
                    }
                    else values[entry.Id] = new(sort.Column == EntrySortColumn.Location ? row.Folder : row.Path);
                });
            }
            var index = await Task.Run(() => EntryViewIndex.Build(store, indexSort,
                EntryFilter.None, WindowsNameComparer.Instance, generation, null, cancellation.Token, propertyValues: values), cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || generation != _generation || !ReferenceEquals(store, _resultStore) || _resultSort != sort) return;
            Results.Bind(store, index, generation); Results.SetSort(sort); UpdateStatusSelection();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { App.LogFailure("SearchResultSort", error); }
        finally { if (ReferenceEquals(_resultSortBuild, cancellation)) _resultSortBuild = null; }
    }
}
