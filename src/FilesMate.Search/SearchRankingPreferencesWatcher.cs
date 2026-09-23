namespace FilesMate.Search;

/// <summary>Observes atomic search-index.json replacements made by either UI process.</summary>
public sealed class SearchRankingPreferencesWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;

    public SearchRankingPreferencesWatcher(string settingsPath, Action changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(changed);
        var fullPath = Path.GetFullPath(settingsPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory)) return;
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            watcher?.Dispose();
            // A watch failure must not stop search or settings from opening.
        }

        void OnChanged(object sender, FileSystemEventArgs args) => changed();
        void OnRenamed(object sender, RenamedEventArgs args) => changed();
        void OnError(object sender, ErrorEventArgs args) => changed();
    }

    public void Dispose() => _watcher?.Dispose();
}
