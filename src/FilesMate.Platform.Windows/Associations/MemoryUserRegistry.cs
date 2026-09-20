namespace FilesMate.Platform.Windows.Associations;

public sealed class MemoryUserRegistry : IUserRegistry
{
    private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public int NotifyCount { get; private set; }

    public string? GetDefaultValue(string subKey) => GetValue(subKey, string.Empty);

    public string? GetValue(string subKey, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        return _keys.TryGetValue(subKey, out var values) && values.TryGetValue(name ?? string.Empty, out var value)
            ? value
            : null;
    }

    public void SetDefaultValue(string subKey, string value) => SetValue(subKey, string.Empty, value);

    public void SetValue(string subKey, string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        ArgumentNullException.ThrowIfNull(value);
        if (!_keys.TryGetValue(subKey, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _keys[subKey] = values;
        }

        values[name ?? string.Empty] = value;
    }

    public void DeleteDefaultValue(string subKey) => DeleteValue(subKey, string.Empty);

    public void DeleteValue(string subKey, string name)
    {
        if (!_keys.TryGetValue(subKey, out var values))
        {
            return;
        }

        values.Remove(name ?? string.Empty);
        if (values.Count == 0)
        {
            _keys.Remove(subKey);
        }
    }

    public void NotifyAssociationsChanged() => NotifyCount++;
}
