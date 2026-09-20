using System.Text.Json;

using FilesMate.App.Models;

namespace FilesMate.App.Services;

public sealed class AppearanceSettingsService : IAppearanceSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public AppearanceSettingsService(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Settings path is required.", nameof(filePath));
        }

        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FilesMate",
            "appearance.json");

    public AppearanceSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppearanceSettings.Default;
            }

            var json = File.ReadAllText(FilePath);
            var dto = JsonSerializer.Deserialize<AppearanceDto>(json, JsonOptions);
            if (dto is null)
            {
                return AppearanceSettings.Default;
            }

            return AppearanceSettings.Sanitize(
                dto.Theme,
                dto.Backdrop,
                dto.ShowStatusBar,
                dto.ShowToolbar,
                dto.ReduceMotion,
                dto.GlassEffect,
                dto.Accent,
                dto.CustomAccent,
                dto.ShellStyle,
                dto.TransparencyPercent,
                dto.UseBundledFileIcons);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppearanceSettings.Default;
        }
    }

    public async Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(
            new AppearanceDto
            {
                Theme = settings.Theme.ToString(),
                Backdrop = settings.Backdrop.ToString(),
                ShowStatusBar = settings.ShowStatusBar,
                ShowToolbar = settings.ShowToolbar,
                ReduceMotion = settings.ReduceMotion.ToString(),
                GlassEffect = settings.GlassEffect.ToString(),
                Accent = settings.Accent.ToString(),
                CustomAccent = settings.CustomAccent,
                ShellStyle = settings.ShellStyle.ToString(),
                TransparencyPercent = settings.TransparencyPercent,
                UseBundledFileIcons = settings.UseBundledFileIcons,
            },
            JsonOptions);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    private sealed class AppearanceDto
    {
        public string? Theme { get; set; }

        public string? Backdrop { get; set; }

        public bool? ShowStatusBar { get; set; }

        public bool? ShowToolbar { get; set; }

        public string? ReduceMotion { get; set; }

        public string? GlassEffect { get; set; }

        public string? Accent { get; set; }

        public string? CustomAccent { get; set; }
        public string? ShellStyle { get; set; }
        public int? TransparencyPercent { get; set; }
        public bool? UseBundledFileIcons { get; set; }
    }
}
