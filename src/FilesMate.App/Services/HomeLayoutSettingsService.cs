using System.Text.Json;

using FilesMate.App.Models;

namespace FilesMate.App.Services;

public sealed class HomeLayoutSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public HomeLayoutSettingsService(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilesMate",
        "home-layout.json");

    public HomeLayoutSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return HomeLayoutSettings.Default;
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath), JsonOptions);
            return dto is null
                ? HomeLayoutSettings.Default
                : HomeLayoutSettings.Sanitize(dto.Order, dto.Hidden);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return HomeLayoutSettings.Default;
        }
    }

    public async Task SaveAsync(HomeLayoutSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(new Dto
        {
            Order = settings.Order.Select(value => value.ToString()).ToArray(),
            Hidden = settings.Hidden.Select(value => value.ToString()).ToArray(),
        }, JsonOptions);
        var temporary = FilePath + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, FilePath, overwrite: true);
    }

    private sealed class Dto
    {
        public string[]? Order { get; set; }

        public string[]? Hidden { get; set; }
    }
}
