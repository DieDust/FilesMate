using System.Text.Json;

using FilesMate.App.Models;

namespace FilesMate.App.Services;

public sealed class ExplorerPreferencesService
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public ExplorerPreferencesService(string filePath)
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
        "explorer.json");

    public static bool LoadOpenInExistingWindow()
    {
        try
        {
            return new ExplorerPreferencesService(DefaultFilePath).Load().OpenInExistingWindow;
        }
        catch
        {
            return ExplorerPreferences.Default.OpenInExistingWindow;
        }
    }

    public ExplorerPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return ExplorerPreferences.Default;
            }

            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath), JsonOptions);
            return dto is null
                ? ExplorerPreferences.Default
                : ExplorerPreferences.Sanitize(
                    dto.ShowHiddenFiles,
                    dto.ConfirmPermanentDelete,
                    dto.OpenFoldersInNewTab,
                    dto.ShowFileExtensions,
                    dto.DefaultView,
                    dto.DateFormat,
                    dto.StartupFolder,
                    dto.SidebarWidth,
                    dto.PreviewWidth,
                    dto.DetailsNameWidth,
                    dto.DetailsModifiedWidth,
                    dto.DetailsTypeWidth,
                    dto.DetailsSizeWidth,
                    dto.DualPane,
                    dto.ShowFolderSizes,
                    dto.RestoreLastSession,
                    dto.OpenInExistingWindow,
                    dto.MixChineseAndLatin,
                    dto.ShowAlphabetNavigation,
                    dto.TabMemory,
                    dto.AlphabetNavigationMinimumItemCount,
                    dto.ShowAlphabetNavigationInDualPane, dto.PaneCount, dto.PaneArrangement,
                    dto.FileOpeningMode, dto.FolderOpeningMode, dto.ShowAlternatingRows, dto.DefaultSortAscending, dto.HiddenToolbarTools,
                    dto.DefaultEntryGrouping, dto.GroupingClickCycle);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return ExplorerPreferences.Default;
        }
    }

    public async Task SaveAsync(ExplorerPreferences settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(new Dto
        {
            ShowHiddenFiles = settings.ShowHiddenFiles,
            ConfirmPermanentDelete = settings.ConfirmPermanentDelete,
            OpenFoldersInNewTab = settings.OpenFoldersInNewTab,
            FileOpeningMode = settings.FileOpeningMode.ToString(),
            FolderOpeningMode = settings.FolderOpeningMode.ToString(),
            ShowFileExtensions = settings.ShowFileExtensions,
            ShowAlternatingRows = settings.ShowAlternatingRows,
            DefaultEntryGrouping = settings.DefaultEntryGrouping.ToString(),
            GroupingClickCycle = settings.GroupingClickCycle?.Select(mode => mode.ToString()).ToArray(),
            DefaultSortAscending = settings.DefaultSortAscending,
            HiddenToolbarTools = settings.HiddenToolbarTools?.Select(tool => tool.ToString()).ToArray(),
            DefaultView = settings.DefaultView.ToString(),
            DateFormat = settings.DateFormat.ToString(),
            StartupFolder = settings.StartupFolder.ToString(),
            SidebarWidth = settings.SidebarWidth,
            PreviewWidth = settings.PreviewWidth,
            DetailsNameWidth = settings.DetailsNameWidth,
            DetailsModifiedWidth = settings.DetailsModifiedWidth,
            DetailsTypeWidth = settings.DetailsTypeWidth,
            DetailsSizeWidth = settings.DetailsSizeWidth,
            DualPane = settings.DualPane,
            PaneCount = settings.PaneCount,
            PaneArrangement = settings.PaneArrangement.ToString(),
            ShowFolderSizes = settings.ShowFolderSizes,
            RestoreLastSession = settings.RestoreLastSession,
            OpenInExistingWindow = settings.OpenInExistingWindow,
            MixChineseAndLatin = settings.MixChineseAndLatin,
            ShowAlphabetNavigation = settings.ShowAlphabetNavigation,
            AlphabetNavigationMinimumItemCount = settings.AlphabetNavigationMinimumItemCount,
            ShowAlphabetNavigationInDualPane = settings.ShowAlphabetNavigationInDualPane,
            TabMemory = settings.TabMemory.ToString(),
        }, JsonOptions);

        await SettingsFileWriter.WriteAsync(FilePath, json, _saveGate, cancellationToken).ConfigureAwait(false);
    }

    private sealed class Dto
    {
        public string? DefaultEntryGrouping { get; set; }
        public string[]? GroupingClickCycle { get; set; }
        public string[]? HiddenToolbarTools { get; set; }
        public bool? ShowAlternatingRows { get; set; }
        public bool? DefaultSortAscending { get; set; }
        public string? FileOpeningMode { get; set; }
        public string? FolderOpeningMode { get; set; }
        public string? TabMemory { get; set; }
        public bool? ShowAlphabetNavigation { get; set; }
        public int? AlphabetNavigationMinimumItemCount { get; set; }
        public bool? ShowAlphabetNavigationInDualPane { get; set; }
        public bool? MixChineseAndLatin { get; set; }
        public bool? ShowHiddenFiles { get; set; }

        public bool? ConfirmPermanentDelete { get; set; }

        public bool? OpenFoldersInNewTab { get; set; }

        public bool? ShowFileExtensions { get; set; }

        public string? DefaultView { get; set; }

        public string? DateFormat { get; set; }

        public string? StartupFolder { get; set; }

        public double? SidebarWidth { get; set; }

        public double? PreviewWidth { get; set; }

        public double? DetailsNameWidth { get; set; }

        public double? DetailsModifiedWidth { get; set; }

        public double? DetailsTypeWidth { get; set; }

        public double? DetailsSizeWidth { get; set; }

        public bool? DualPane { get; set; }
        public int? PaneCount { get; set; }
        public string? PaneArrangement { get; set; }

        public bool? ShowFolderSizes { get; set; }

        public bool? RestoreLastSession { get; set; }

        public bool? OpenInExistingWindow { get; set; }
    }
}
