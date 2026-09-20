using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.Core.Entries;
using FilesMate.App.Models;

namespace FilesMate.App.Services;

public sealed record FolderViewSettings(bool Details, int GridSlot, EntrySort Sort, DetailsColumn[]? Columns = null);
public sealed record FolderCustomization(FolderViewSettings? View = null, string? CoverPath = null);
public sealed record FolderViewScope(bool Global = false, FolderViewSettings? View = null, bool Initialized = false);

public sealed class FolderCustomizationStore
{
    private const int Capacity = 2048;
    private readonly string _file;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<string, FolderCustomization> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _loaded;
    private long _revision;
    private long _savedRevision;
    private FolderViewScope _scope = new();
    private string ScopeFile => Path.ChangeExtension(_file, ".view-scope.json");
    public event EventHandler? ViewSettingsChanged;

    public async Task<bool> GetGlobalViewAsync()
    {
        await _loaded.ConfigureAwait(false);
        lock (_sync) return _scope.Global;
    }

    public async Task SetGlobalViewAsync(bool global)
    {
        await _loaded.ConfigureAwait(false);
        lock (_sync)
        {
            if (_scope.Global == global) return;
            _scope = _scope with { Global = global, View = global && !_scope.Initialized ? _items.Values.LastOrDefault(c => c.View is not null)?.View : _scope.View, Initialized = _scope.Initialized || global };
            _revision++;
        }
        await FlushAsync().ConfigureAwait(false);
        ViewSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public FolderCustomizationStore(string file)
    {
        _file = file;
        _loaded = Task.Run(Load);
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate", "folder-customizations.json");

    public static string? Key(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains("://", StringComparison.Ordinal))
            return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (ArgumentException) { return null; }
    }

    public async Task<FolderCustomization> GetAsync(string path)
    {
        await _loaded.ConfigureAwait(false);
        var key = Key(path);
        lock (_sync)
        {
            var result = key is not null && _items.TryGetValue(key, out var value) ? value : new();
            return _scope.Global ? result with { View = _scope.View } : result;
        }
    }

    public async Task SetViewAsync(string path, FolderViewSettings? view)
    {
        await _loaded.ConfigureAwait(false);
        bool global;
        lock (_sync)
        {
            global = _scope.Global;
            if (global)
            {
                if (_scope.View == view && _revision == _savedRevision) return;
                _scope = _scope with { View = view };
                _revision++;
            }
        }
        if (!global) { await UpdateAsync(path, current => current with { View = view }); return; }
        await Task.Delay(200).ConfigureAwait(false);
        await FlushAsync().ConfigureAwait(false);
        ViewSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SetCoverAsync(string path, string? cover)
    {
        if (cover is not null && (Key(cover) is null
            || !string.Equals(Key(Path.GetDirectoryName(cover)), Key(path), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The cover must be a file directly inside this folder.", nameof(cover));
        return UpdateAsync(path, current => current with { CoverPath = cover });
    }

    private async Task UpdateAsync(string path, Func<FolderCustomization, FolderCustomization> change)
    {
        var key = Key(path) ?? throw new ArgumentException("A local or network folder path is required.", nameof(path));
        await _loaded.ConfigureAwait(false);
        lock (_sync)
        {
            var current = _items.GetValueOrDefault(key) ?? new();
            var next = change(current);
            if (next == current && _revision == _savedRevision)
                return;
            _items.Remove(key);
            if (next.View is not null || next.CoverPath is not null)
                _items[key] = next;
            while (_items.Count > Capacity)
                _items.Remove(_items.Keys.First());
            _revision++;
        }

        // Zoom gestures often emit several changes. Persist only the latest snapshot.
        await Task.Delay(200).ConfigureAwait(false);
        await FlushAsync().ConfigureAwait(false);
    }

    public async Task FlushAsync()
    {
        await _loaded.ConfigureAwait(false);
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Dictionary<string, FolderCustomization> snapshot;
            FolderViewScope scope;
            long revision;
            lock (_sync)
            {
                if (_savedRevision == _revision)
                    return;
                revision = _revision;
                snapshot = new(_items, StringComparer.OrdinalIgnoreCase);
                scope = _scope;
            }
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var temporary = _file + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
                File.Move(temporary, _file, overwrite: true);
                var scopeTemporary = ScopeFile + ".tmp";
                File.WriteAllText(scopeTemporary, JsonSerializer.Serialize(scope));
                File.Move(scopeTemporary, ScopeFile, overwrite: true);
            }).ConfigureAwait(false);
            lock (_sync) _savedRevision = revision;
        }
        finally { _writeGate.Release(); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var values = JsonSerializer.Deserialize<Dictionary<string, FolderCustomization>>(File.ReadAllText(_file));
            if (values is null) return;
            foreach (var (path, item) in values.TakeLast(Capacity))
            {
                var key = Key(path);
                if (key is null || item is null) continue;
                var view = item.View;
                if (view?.Sort is null || !Enum.IsDefined(view.Sort.Column))
                    view = null;
                if (view is not null && !GridSizePreset.All.Any(preset => preset.Slot == view.GridSlot))
                    view = view with { GridSlot = GridSizePreset.Default.Slot };
                var cover = item.CoverPath;
                if (cover is not null && !string.Equals(Key(Path.GetDirectoryName(cover)), key, StringComparison.OrdinalIgnoreCase))
                    cover = null;
                _items[key] = item with { View = view, CoverPath = cover };
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _items.Clear();
        }
        finally
        {
            try
            {
                if (File.Exists(ScopeFile))
                {
                    _scope = JsonSerializer.Deserialize<FolderViewScope>(File.ReadAllText(ScopeFile)) ?? new();
                    if (_scope.View is { } view && (view.Sort is null || !Enum.IsDefined(view.Sort.Column))) _scope = _scope with { View = null };
                    else if (_scope.View is { } valid && !GridSizePreset.All.Any(p => p.Slot == valid.GridSlot)) _scope = _scope with { View = valid with { GridSlot = GridSizePreset.Default.Slot } };
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { _scope = new(); }
        }
    }
}
