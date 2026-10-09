using System.Text.Json;

namespace FilesMate.App.Services;

public sealed record WindowSession(IReadOnlyList<string> Tabs, int SelectedTabIndex);

/// <summary>
/// Persists the last open tab set, following the Files app session snapshot pattern.
/// </summary>
public sealed class WindowSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public WindowSessionStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Session path is required.", nameof(filePath));
        }

        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultFilePath =>
        Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath,
            "window-session.json");

    public WindowSession Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return Empty();
            }

            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath), JsonOptions);
            if (dto?.Tabs is null || dto.Tabs.Length == 0)
            {
                return Empty();
            }

            var tabs = NormalizeTabs(dto.Tabs, dto.SelectedTabIndex ?? 0, out var selected);
            return new WindowSession(tabs, selected);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Empty();
        }
    }

    public void Save(WindowSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var tabs = NormalizeTabs(session.Tabs, session.SelectedTabIndex, out var selected);
        if (tabs.Length == 0)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new Dto
        {
            Tabs = tabs,
            SelectedTabIndex = selected,
        }, JsonOptions);
        WriteAtomic(json);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("Session clear failed: {0}", ex);
        }
    }

    private void WriteAtomic(string json)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    private static string[] NormalizeTabs(IReadOnlyList<string> paths, int selectedTabIndex, out int selected)
    {
        var tabs = new List<string>(paths.Count);
        // Duplicate folders are distinct tabs. Keep their order and map the original
        // selected ordinal while filtering blanks; a removed/invalid selection uses the first tab.
        selected = 0;
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (i == selectedTabIndex)
            {
                selected = tabs.Count;
            }
            tabs.Add(path.Trim());
        }

        return tabs.ToArray();
    }

    private static WindowSession Empty() => new([], 0);

    private sealed class Dto
    {
        public string[]? Tabs { get; init; }

        public int? SelectedTabIndex { get; init; }
    }
}
