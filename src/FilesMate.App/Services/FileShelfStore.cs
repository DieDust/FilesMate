using System.Text.Json;

namespace FilesMate.App.Services;

public sealed class FileShelfStore
{
    public const int Capacity = 512;
    private readonly string _file;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _paths = [];
    private readonly Task _loaded;
    public event EventHandler? Changed;

    public FileShelfStore(string file)
    {
        _file = file;
        _loaded = Task.Run(() =>
        {
            try
            {
                if (File.Exists(file))
                    _paths.AddRange(Normalize(JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? []).Take(Capacity));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        });
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate", "file-shelf.json");

    public async Task<IReadOnlyList<string>> GetAsync()
    {
        await _loaded.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return _paths.ToArray(); }
        finally { _gate.Release(); }
    }

    public Task AddAsync(IEnumerable<string> paths) => ChangeAsync(paths.ToArray(), remove: false);
    public Task RemoveAsync(IEnumerable<string> paths) => ChangeAsync(paths.ToArray(), remove: true);

    private async Task ChangeAsync(string[] paths, bool remove)
    {
        await _loaded.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var normalized = Normalize(paths);
            var next = remove
                ? _paths.Except(normalized, StringComparer.OrdinalIgnoreCase).ToArray()
                : _paths.Concat(normalized).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (next.Length > Capacity)
                throw new InvalidOperationException(FilesMate.App.Localization.StringTable.Format("Shelf_Capacity", Capacity));
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(next));
                File.Move(_file + ".tmp", _file, overwrite: true);
            }).ConfigureAwait(false);
            _paths.Clear();
            _paths.AddRange(next);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string[] Normalize(IEnumerable<string> paths) =>
        paths.Select(FolderCustomizationStore.Key).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(Capacity + 1).ToArray();
}
