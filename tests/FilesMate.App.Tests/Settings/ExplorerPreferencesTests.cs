using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Settings;

public sealed class ExplorerPreferencesTests
{
    [Fact]
    public void Missing_or_corrupt_json_returns_defaults()
    {
        var missing = new ExplorerPreferencesService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "explorer.json"));
        Assert.Equal(ExplorerPreferences.Default, missing.Load());

        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{ not json");
        Assert.Equal(ExplorerPreferences.Default, new ExplorerPreferencesService(file).Load());
    }

    [Fact]
    public void Unknown_enum_values_fall_back_and_legacy_files_keep_working()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(
            file,
            """
            {
              "showHiddenFiles": true,
              "confirmPermanentDelete": false,
              "openFoldersInNewTab": true
            }
            """);

        var loaded = new ExplorerPreferencesService(file).Load();
        Assert.True(loaded.ShowHiddenFiles);
        Assert.False(loaded.ConfirmPermanentDelete);
        Assert.True(loaded.OpenFoldersInNewTab);
        Assert.True(loaded.RestoreLastSession);
        Assert.True(loaded.OpenInExistingWindow);
        Assert.True(loaded.ShowFileExtensions);
        Assert.False(loaded.DualPane);
        Assert.False(loaded.ShowFolderSizes);
        Assert.False(loaded.ShowAlphabetNavigation);
        Assert.Equal(20, loaded.AlphabetNavigationMinimumItemCount);
        Assert.False(loaded.ShowAlphabetNavigationInDualPane);
        Assert.Equal(FolderViewKind.Details, loaded.DefaultView);
        Assert.Equal(DateFormatKind.System, loaded.DateFormat);
        Assert.Equal(FolderStartupKind.Desktop, loaded.StartupFolder);
        Assert.Equal(236, loaded.SidebarWidth);
        Assert.Equal(320, loaded.PreviewWidth);
        Assert.Equal(240, loaded.DetailsNameWidth);
        Assert.Equal(148, loaded.DetailsModifiedWidth);
        Assert.Equal(52, ExplorerPreferences.ClampSidebarWidth(10));
        Assert.Equal(360, ExplorerPreferences.ClampSidebarWidth(900));
        Assert.Equal(200, ExplorerPreferences.ClampPreviewWidth(10));
        Assert.Equal(640, ExplorerPreferences.ClampPreviewWidth(900));
        Assert.True(ExplorerPreferences.SidebarIsCompact(52));
        Assert.False(ExplorerPreferences.SidebarIsCompact(236));
        Assert.Equal(
            FolderViewKind.Details,
            ExplorerPreferences.Sanitize(false, true, false, true, "Details", "Iso", "Home").DefaultView);
        Assert.Equal(
            FolderViewKind.Details,
            ExplorerPreferences.Sanitize(false, true, false, true, "Tiles", "Iso", "Home").DefaultView);
    }

    [Fact]
    public async Task Save_replaces_via_temp_file_and_round_trips()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "explorer.json");
        var service = new ExplorerPreferencesService(file);
        var settings = ExplorerPreferences.Default with
        {
            ShowHiddenFiles = true,
            ShowFileExtensions = false,
            DefaultView = FolderViewKind.Details,
            DateFormat = DateFormatKind.Iso,
            StartupFolder = FolderStartupKind.Home,
            DualPane = true,
            ShowFolderSizes = true,
            AlphabetNavigationMinimumItemCount = 35,
            ShowAlphabetNavigationInDualPane = true,
        };

        await service.SaveAsync(settings);

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        var json = await File.ReadAllTextAsync(file);
        Assert.Contains("\"showFileExtensions\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"defaultView\": \"Details\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dualPane\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"showFolderSizes\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"alphabetNavigationMinimumItemCount\": 35", json, StringComparison.Ordinal);
        Assert.Contains("\"showAlphabetNavigationInDualPane\": true", json, StringComparison.Ordinal);
        Assert.Equal(settings, service.Load());
        Assert.Contains("File.Replace", File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Services",
            "ExplorerPreferencesService.cs")), StringComparison.Ordinal);
    }
}
