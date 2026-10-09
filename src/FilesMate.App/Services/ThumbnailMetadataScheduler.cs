namespace FilesMate.App.Services;

/// <summary>
/// Bounds synchronous thumbnail metadata probes, including slow network FileInfo
/// and optional cover-index lookups. Cancellation can retire an awaiting row, but
/// a running blocking call owns its slot until that call actually returns.
/// </summary>
internal static class ThumbnailMetadataScheduler
{
    private static readonly SemaphoreSlim Slots = new(2, 2);

    internal static async Task<T> RunAsync<T>(Func<T> read, CancellationToken token)
    {
        await Slots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var result = await Task.Run(read, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { Slots.Release(); }
    }
}
