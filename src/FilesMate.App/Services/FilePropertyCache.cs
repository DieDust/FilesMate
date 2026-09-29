using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Services;

/// <summary>Bounded metadata cache; the default four-column path never enters a property handler.</summary>
public static class FilePropertyCache
{
    private sealed record Key(string Path, long Modified, ulong Size, string Properties);
    private sealed record Cached(DateTime Time, IReadOnlyDictionary<string, EntryPropertyValue> Values);
    private static readonly Dictionary<Key, Cached> Cache = [];
    private static readonly Queue<Key> Order = [];
    private static readonly object Gate = new();
    public static async Task<IReadOnlyDictionary<string, EntryPropertyValue>> GetAsync(string path, FileEntryCore entry,
        IReadOnlyList<string> properties, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new Dictionary<string, EntryPropertyValue>();
        var key = new Key(path.ToUpperInvariant(), entry.ModifiedUtcTicks, entry.Size, string.Join('|', properties.Order(StringComparer.Ordinal)));
        lock (Gate)
            if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromSeconds(30)) return cached.Values;
        var values = await ShellProperties.ReadAsync(path, properties, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (Gate)
        {
            if (!Cache.ContainsKey(key)) Order.Enqueue(key);
            Cache[key] = new(DateTime.UtcNow, values);
            while (Cache.Count > 512 && Order.TryDequeue(out var oldest)) Cache.Remove(oldest);
        }
        return values;
    }
}
