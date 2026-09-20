using System.Text.Json;

using FilesMate.App.Models;

namespace FilesMate.App.Services;

public sealed class WindowPlacementService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public WindowPlacementService(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Settings path is required.", nameof(filePath));
        }

        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilesMate",
        "window.json");

    public WindowPlacement Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return WindowPlacement.Default;
            }

            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath), JsonOptions);
            if (dto is null)
            {
                return WindowPlacement.Default;
            }

            return new WindowPlacement(
                dto.X ?? WindowPlacement.Unset,
                dto.Y ?? WindowPlacement.Unset,
                dto.Width ?? WindowPlacement.DefaultWidth,
                dto.Height ?? WindowPlacement.DefaultHeight,
                dto.Maximized == true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return WindowPlacement.Default;
        }
    }

    public void Save(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var json = JsonSerializer.Serialize(
            new Dto
            {
                X = placement.X,
                Y = placement.Y,
                Width = placement.Width,
                Height = placement.Height,
                Maximized = placement.Maximized,
            },
            JsonOptions);

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

    private sealed class Dto
    {
        public int? X { get; set; }

        public int? Y { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public bool? Maximized { get; set; }
    }
}
