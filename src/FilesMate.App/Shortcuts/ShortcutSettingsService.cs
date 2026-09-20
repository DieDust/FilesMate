using System.Text.Json;

namespace FilesMate.App.Shortcuts;

public sealed class ShortcutSettingsService
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilesMate",
        "shortcuts.json");

    private readonly string _filePath;

    public ShortcutSettingsService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public ShortcutMap Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new ShortcutMap();
            }

            var json = File.ReadAllText(_filePath);
            var values = JsonSerializer.Deserialize<Dictionary<string, PersistedGesture>>(json)
                ?? [];
            var parsed = new List<KeyValuePair<ShortcutAction, ShortcutGesture>>();
            foreach (var (name, value) in values)
            {
                if (Enum.TryParse<ShortcutAction>(name, out var action))
                {
                    parsed.Add(new KeyValuePair<ShortcutAction, ShortcutGesture>(
                        action,
                        new ShortcutGesture((ShortcutKey)value.Key, (ShortcutModifiers)value.Modifiers)));
                }
            }

            return new ShortcutMap(parsed);
        }
        catch (Exception) when (
            !System.Diagnostics.Debugger.IsAttached)
        {
            return new ShortcutMap();
        }
    }

    public async Task SaveAsync(ShortcutMap map, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var values = map.Snapshot().ToDictionary(
            pair => pair.Key.ToString(),
            pair => new PersistedGesture((int)pair.Value.Key, (int)pair.Value.Modifiers),
            StringComparer.Ordinal);
        await using var stream = new FileStream(
            _filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true);
        await JsonSerializer.SerializeAsync(stream, values, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record PersistedGesture(int Key, int Modifiers);
}
