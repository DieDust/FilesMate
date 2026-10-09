using FilesMate.App.Services;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Views;

public sealed partial class SearchResultsPage
{
    private CancellationTokenSource? _metadataLifetime;
    private Task _metadataTail = Task.CompletedTask;

    private void CancelResultMetadata()
    {
        _metadataLifetime?.Cancel();
        _metadataLifetime?.Dispose();
        _metadataLifetime = null;
        _metadataTail = Task.CompletedTask;
    }

    private void QueueResultMetadata(SearchResultRow[] rows, int offset)
    {
        if (rows.Length == 0) return;
        _metadataLifetime ??= new CancellationTokenSource();
        var token = _metadataLifetime.Token;
        var store = _resultStore;
        var generation = _generation;
        // Serialize pages, with at most three global reads, and no per-row UI callback.
        _metadataTail = HydrateResultPageAsync(_metadataTail, rows, offset, store, generation, token);
    }

    private async Task HydrateResultPageAsync(Task previous, SearchResultRow[] rows, int offset,
        EntryStore store, long generation, CancellationToken token)
    {
        try
        {
            await previous;
            token.ThrowIfCancellationRequested();
            var requests = rows.Select((row, i) => new SearchMetadataRequest(
                row.Hit.Application is null ? row.Path : string.Empty, store[offset + i])).ToArray();
            var metadata = await SearchResultMetadataLoader.ReadAsync(requests, token);
            if (_disposed || token.IsCancellationRequested || !IsLoaded || generation != _generation || !ReferenceEquals(store, _resultStore)) return;
            // Do not interrupt marquee, drag/drop, column resize or inline rename.
            while (Results.IsMemoryReclamationBusy)
            {
                await Task.Delay(50, token);
                if (_disposed || !IsLoaded || generation != _generation || !ReferenceEquals(store, _resultStore)) return;
            }
            var changed = false;
            for (var i = 0; i < rows.Length; i++)
                if (offset + i < _rows.Length && ReferenceEquals(rows[i], _rows[offset + i]))
                    changed |= store.ReplaceAt(offset + i, metadata[i]);
            if (!changed) return;
            if (_resultSort is not null) await ApplyResultSortAsync();
            else
            {
                var scroll = Results.ScrollOffset;
                Results.Bind(store, EntryViewIndex.InSourceOrder(store, generation, SurfaceSort()), generation);
                Results.RestoreScrollOffset(scroll, preserveExactOffset: true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { App.LogFailure("SearchResultMetadata", error); }
    }
}
