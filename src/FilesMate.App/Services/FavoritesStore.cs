using Loc = FilesMate.App.Localization.StringTable;
using System.Text.Json;

namespace FilesMate.App.Services;

public sealed record FavoriteEntry(string Id, string Name, string? Path, bool IsDirectory, string? GroupId = null)
{
    public bool IsGroup => Path is null;
}

/// <summary>References only: never enumerates, decodes or watches the bookmarked files.</summary>
public sealed class FavoritesStore
{
    public const int Capacity = 2000;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<FavoriteEntry> _entries = Array.Empty<FavoriteEntry>();
    public IReadOnlyList<FavoriteEntry> Entries => _entries;
    public event EventHandler? Changed;
    public string? LoadError { get; private set; }

    public FavoritesStore(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidOperationException(Loc.Get("Favorites_DataTooLarge"));
            var entries = JsonSerializer.Deserialize<List<FavoriteEntry>>(File.ReadAllText(path)) ?? [];
            Validate(entries);
            _entries = entries.AsReadOnly();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Preserve a damaged file for recovery rather than overwriting it with an empty list.
            LoadError = Loc.Get("Favorites_ReadFailed") + error.Message;
        }
    }

    public async Task AddAsync(IEnumerable<(string Path, bool IsDirectory)> targets, string? groupId = null)
    {
        var batch = targets.ToArray();
        await ChangeAsync(entries =>
        {
            RequireGroup(entries, groupId);
            foreach (var target in batch)
            {
                if (!System.IO.Path.IsPathFullyQualified(target.Path)) throw new InvalidOperationException(Loc.Get("Favorites_LocalOnly"));
                var path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(target.Path));
                if (entries.Any(entry => entry.GroupId == groupId && string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                var name = System.IO.Path.GetFileName(path);
                name = string.IsNullOrEmpty(name) ? path : name;
                entries.Add(new(Guid.NewGuid().ToString("N"), name.Length > 120 ? name[..120] : name, path, target.IsDirectory, groupId));
            }
        });
    }

    /// <summary>The quick-save button reuses an existing bookmark, including one in a group.</summary>
    public async Task<FavoriteEntry> SaveFolderAsync(string folder)
    {
        if (!System.IO.Path.IsPathFullyQualified(folder)) throw new InvalidOperationException(Loc.Get("OpenFolderFirst"));
        var path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
        FavoriteEntry? saved = null;
        await ChangeAsync(entries =>
        {
            saved = entries.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
            if (saved is not null) return;
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            saved = new(Guid.NewGuid().ToString("N"), name.Length > 120 ? name[..120] : name, path, true);
            entries.Add(saved);
        });
        return saved!;
    }

    public Task CreateGroupAsync(string name) => ChangeAsync(entries =>
        entries.Add(new(Guid.NewGuid().ToString("N"), CleanName(name), null, true)));

    public Task RenameAsync(string id, string name) => ChangeAsync(entries =>
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        if (index >= 0) entries[index] = entries[index] with { Name = CleanName(name) };
    });

    public Task RemoveAsync(string id) => ChangeAsync(entries => entries.RemoveAll(entry => entry.Id == id || entry.GroupId == id));

    public Task RemoveManyAsync(IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        return ChangeAsync(entries => entries.RemoveAll(entry => selected.Contains(entry.Id) || (entry.GroupId is { } parent && selected.Contains(parent))));
    }

    public Task MoveManyAsync(IEnumerable<string> ids, string? groupId)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        return ChangeAsync(entries =>
        {
            RequireGroup(entries, groupId);
            var moving = entries.Where(entry => selected.Contains(entry.Id)).ToArray();
            if (moving.Length != selected.Count) throw new InvalidOperationException(Loc.Get("Favorites_MissingItems"));
            if (groupId is not null && moving.Any(entry => entry.IsGroup)) throw new InvalidOperationException(Loc.Get("Favorites_GroupAtRoot"));
            var paths = entries.Where(entry => entry.GroupId == groupId && !selected.Contains(entry.Id) && entry.Path is not null)
                .Select(entry => entry.Path!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in moving)
                if (entry.Path is { } path && !paths.Add(path)) throw new InvalidOperationException(Loc.Get("Favorites_DuplicateTarget"));
            entries.RemoveAll(entry => selected.Contains(entry.Id));
            entries.AddRange(moving.Select(entry => entry with { GroupId = groupId }));
        });
    }

    public Task SetOrderAsync(string? groupId, IReadOnlyList<string> ids) => ChangeAsync(entries =>
    {
        var siblings = entries.Where(entry => entry.GroupId == groupId).ToDictionary(entry => entry.Id);
        if (ids.Count != siblings.Count || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Any(id => !siblings.ContainsKey(id)))
            throw new InvalidOperationException(Loc.Get("Favorites_Changed"));
        var next = 0;
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].GroupId == groupId) entries[i] = siblings[ids[next++]];
    });

    public Task MoveAsync(string id, string? groupId, string? beforeId = null) => ChangeAsync(entries =>
    {
        RequireGroup(entries, groupId);
        var entry = entries.FirstOrDefault(entry => entry.Id == id) ?? throw new InvalidOperationException(Loc.Get("Favorites_MissingItem"));
        if (entry.IsGroup && groupId is not null) throw new InvalidOperationException(Loc.Get("Favorites_GroupAtRoot"));
        if (id == beforeId) return;
        if (entries.Any(other => other.Id != id && other.GroupId == groupId && entry.Path is not null && string.Equals(other.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(Loc.Get("Favorites_AlreadyInGroup"));
        entries.Remove(entry);
        var index = beforeId is null ? -1 : entries.FindIndex(other => other.Id == beforeId && other.GroupId == groupId);
        if (index < 0) entries.Add(entry with { GroupId = groupId });
        else entries.Insert(index, entry with { GroupId = groupId });
    });

    public Task ReorderAsync(string id, int direction) => ChangeAsync(entries =>
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return;
        var siblings = entries.Select((entry, i) => (entry, i)).Where(pair => pair.entry.GroupId == entries[index].GroupId).Select(pair => pair.i).ToArray();
        var next = Array.IndexOf(siblings, index) + Math.Sign(direction);
        if (next >= 0 && next < siblings.Length) (entries[index], entries[siblings[next]]) = (entries[siblings[next]], entries[index]);
    });

    private async Task ChangeAsync(Action<List<FavoriteEntry>> edit)
    {
        await _gate.WaitAsync();
        try
        {
            if (LoadError is not null) throw new IOException(LoadError);
            var next = _entries.ToList();
            edit(next);
            Validate(next);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next));
                File.Move(temporary, _path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            _entries = next.AsReadOnly();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string CleanName(string name) => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120
        ? throw new InvalidOperationException(Loc.Get("Favorites_NameLength")) : name.Trim();

    private static void RequireGroup(List<FavoriteEntry> entries, string? groupId)
    {
        if (groupId is not null && !entries.Any(entry => entry.Id == groupId && entry.IsGroup))
            throw new InvalidOperationException(Loc.Get("Favorites_MissingGroup"));
    }

    private static void Validate(List<FavoriteEntry> entries)
    {
        if (entries.Count > Capacity) throw new InvalidOperationException(Loc.Format("Favorites_Capacity", Capacity));
        var ids = new HashSet<string>();
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Id) || !ids.Add(entry.Id)) throw new InvalidOperationException(Loc.Get("Favorites_InvalidId"));
            CleanName(entry.Name);
            if (entry.IsGroup && entry.GroupId is not null) throw new InvalidOperationException(Loc.Get("Favorites_InvalidNesting"));
            if (!entry.IsGroup && !System.IO.Path.IsPathFullyQualified(entry.Path!)) throw new InvalidOperationException(Loc.Get("Favorites_InvalidPath"));
        }
        foreach (var entry in entries) RequireGroup(entries, entry.GroupId);
    }
}
