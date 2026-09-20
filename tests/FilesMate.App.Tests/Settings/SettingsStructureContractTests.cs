using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Settings;

public sealed class SettingsStructureContractTests
{
    [Fact]
    public void Settings_shell_lazily_hosts_only_functional_sections()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SettingsPage.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("x:Name=\"SectionHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"files-folders\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"search\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"keyboard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"advanced\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SearchNavLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesAndFoldersNavLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("AdvancedNavLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, UIElement>", code, StringComparison.Ordinal);
        Assert.Contains("CreateSection", code, StringComparison.Ordinal);
        Assert.Contains("ShowPendingCategory", code, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue(ShowPendingCategory)", code, StringComparison.Ordinal);
        Assert.Contains("new FilesAndFoldersSettingsPage()", code, StringComparison.Ordinal);
        Assert.Contains("new SearchSettingsPage()", code, StringComparison.Ordinal);
        Assert.Contains("new ShortcutsSettingsPage()", code, StringComparison.Ordinal);
        Assert.Contains("new AdvancedSettingsPage()", code, StringComparison.Ordinal);
        Assert.Contains("\"multitasking\" => \"general\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void General_and_tag_sections_contain_real_actions()
    {
        var general = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "GeneralPage.xaml"));
        var tags = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "TagManagementPage.xaml"));
        var tagCode = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "TagManagementPage.xaml.cs"));

        Assert.DoesNotContain("What works now", general, StringComparison.Ordinal);
        Assert.Contains("StartupBox", general, StringComparison.Ordinal);
        Assert.Contains("RestoreSessionToggle", general, StringComparison.Ordinal);
        Assert.Contains("OpenInExistingWindowToggle", general, StringComparison.Ordinal);
        Assert.Contains("OpenFoldersToggle", general, StringComparison.Ordinal);
        Assert.DoesNotContain("ShortcutRows", general, StringComparison.Ordinal);
        Assert.Contains("NewTag", tags, StringComparison.Ordinal);
        Assert.Contains("EditTag_Click", tagCode, StringComparison.Ordinal);
        Assert.Contains("DeleteTag_Click", tagCode, StringComparison.Ordinal);
        Assert.Contains("TagEditor.ShowAsync", tagCode, StringComparison.Ordinal);
        Assert.Contains("UpdateTagAsync", tagCode, StringComparison.Ordinal);
        Assert.Contains("DeleteTagAsync", tagCode, StringComparison.Ordinal);

        var files = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "FilesAndFoldersSettingsPage.xaml"));
        var filesCode = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "FilesAndFoldersSettingsPage.xaml.cs"));
        var keyboard = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "ShortcutsSettingsPage.xaml"));
        var keyboardCode = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "ShortcutsSettingsPage.xaml.cs"));
        var advanced = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "AdvancedSettingsPage.xaml"));
        var search = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SearchSettingsPage.xaml"));
        var searchCode = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SearchSettingsPage.xaml.cs"));
        Assert.Contains("HiddenFilesToggle", files, StringComparison.Ordinal);
        Assert.Contains("ConfirmDeleteToggle", files, StringComparison.Ordinal);
        Assert.Contains("ExtensionsToggle", files, StringComparison.Ordinal);
        Assert.Contains("DefaultViewBox", files, StringComparison.Ordinal);
        Assert.Contains("DateFormatBox", files, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FolderSizesToggle\"", files, StringComparison.Ordinal);
        Assert.Contains("ShowFolderSizes", filesCode, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlphabetHeader\"", files, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlphabetMinimumItemsBox\"", files, StringComparison.Ordinal);
        Assert.Contains("SpinButtonPlacementMode=\"Hidden\"", files, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlphabetDualPaneToggle\"", files, StringComparison.Ordinal);
        Assert.Contains("AlphabetNavigationMinimumItemCount", filesCode, StringComparison.Ordinal);
        Assert.Contains("ShowAlphabetNavigationInDualPane", filesCode, StringComparison.Ordinal);
        Assert.Contains("RestoreSidebarButton", files, StringComparison.Ordinal);
        Assert.Contains("RestoreDefaults", filesCode, StringComparison.Ordinal);
        Assert.Contains("NotifyPinnedLocationsChanged", filesCode, StringComparison.Ordinal);
        Assert.Contains("OpenFoldersToggle", general, StringComparison.Ordinal);
        Assert.Contains("RestoreSessionToggle", general, StringComparison.Ordinal);
        Assert.Contains("OpenInExistingWindowToggle", general, StringComparison.Ordinal);
        Assert.Contains("ShortcutRows", keyboard, StringComparison.Ordinal);
        Assert.Contains("ShortcutsLead", keyboard, StringComparison.Ordinal);
        Assert.Contains("RestoreDefaultsButton", keyboard, StringComparison.Ordinal);
        Assert.Contains("UpdateShortcutAsync", keyboardCode, StringComparison.Ordinal);
        Assert.Contains("DISABLE_XAML_GENERATED_MAIN", File.ReadAllText(Path.Combine(ThemeXaml.RepoRoot, "src", "FilesMate.App", "FilesMate.App.csproj")), StringComparison.Ordinal);
        Assert.Contains("AppLifecycle.TryBecomeMainInstance", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Program.cs")), StringComparison.Ordinal);
        Assert.Contains("TryParkWorkingDirectory", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Program.cs")), StringComparison.Ordinal);
        Assert.Contains("WindowSessionStore", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("UpdateTaskbarTitle", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("TrySelectByName", File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("ClearCacheButton", advanced, StringComparison.Ordinal);
        Assert.Contains("OpenDataFolderButton", advanced, StringComparison.Ordinal);
        Assert.Contains("DefaultAppToggle", advanced, StringComparison.Ordinal);
        Assert.Contains("ClassicExplorerButton", advanced, StringComparison.Ordinal);
        var advancedCode = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "AdvancedSettingsPage.xaml.cs"));
        Assert.Contains("HasOurCommand", advancedCode, StringComparison.Ordinal);
        Assert.Contains("_association.Enable", advancedCode, StringComparison.Ordinal);
        Assert.Contains("ClassicExplorer.Launch", advancedCode, StringComparison.Ordinal);
        Assert.Contains("Launch(_association, exe)", advancedCode, StringComparison.Ordinal);
        var classic = File.ReadAllText(Path.Combine(
            ThemeXaml.RepoRoot,
            "src",
            "FilesMate.Platform.Windows",
            "Associations",
            "ClassicExplorer.cs"));
        Assert.Contains("UseShellExecute = false", classic, StringComparison.Ordinal);
        Assert.Contains("explorer.exe", classic, StringComparison.Ordinal);
        Assert.Contains("TryLaunchViaExecuteFolder", classic, StringComparison.Ordinal);
        Assert.Contains("CabinetWClass", classic, StringComparison.Ordinal);
        Assert.Contains("RunWhileSuspended", classic, StringComparison.Ordinal);
        Assert.Contains("SeparateArguments = \"/n,/separate\"", classic, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellExecuteEx", classic, StringComparison.Ordinal);
        Assert.DoesNotContain("opennewprocess", classic, StringComparison.OrdinalIgnoreCase);
        var app = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml.cs"));
        Assert.Contains("RepairFolderHandlers", app, StringComparison.Ordinal);
        Assert.Contains("HasOurCommand(exe)", app, StringComparison.Ordinal);
        Assert.Contains("association.Enable(exe)", app, StringComparison.Ordinal);
        Assert.Contains("IsExplorerHost", app, StringComparison.Ordinal);
        Assert.Contains("LaunchClassicExplorer", app, StringComparison.Ordinal);
        Assert.Contains("AppUserModel.RegisterCurrentProcess", app, StringComparison.Ordinal);
        var association = File.ReadAllText(Path.Combine(
            ThemeXaml.RepoRoot,
            "src",
            "FilesMate.Platform.Windows",
            "Associations",
            "DefaultFolderAssociation.cs"));
        Assert.Contains("ExplorerAppPathsKey", association, StringComparison.Ordinal);
        Assert.Contains("RemoveOverlay(WinEClass, \"opennewwindow\")", association, StringComparison.Ordinal);
        Assert.DoesNotContain("new(WinEClass, \"opennewwindow\"", association, StringComparison.Ordinal);
        Assert.Contains("IndexButton", search, StringComparison.Ordinal);
        Assert.Contains("DriveHost", search, StringComparison.Ordinal);
        Assert.Contains("DepthBox", search, StringComparison.Ordinal);
        Assert.Contains("Tag=\"6\"", search, StringComparison.Ordinal);
        Assert.Contains("IndexLocationCard", search, StringComparison.Ordinal);
        Assert.Contains("ChangeLocationButton", search, StringComparison.Ordinal);
        Assert.Contains("AutoRefreshToggle", search, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RankList\"", search, StringComparison.Ordinal);
        Assert.Contains("CanReorderItems=\"True\"", search, StringComparison.Ordinal);
        Assert.Contains("RankList_DragItemsCompleted", searchCode, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelButton.Visibility", searchCode, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue(() => _ = StartIndex())", searchCode, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelButton.Visibility", searchCode, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerationCard", advanced, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryCard", advanced, StringComparison.Ordinal);
        Assert.DoesNotContain("KnownFoldersCard", files, StringComparison.Ordinal);
        Assert.DoesNotContain("TabStateCard", general, StringComparison.Ordinal);

        Assert.Contains("StartupBox", general, StringComparison.Ordinal);
        Assert.Contains("SafetyHeader", files, StringComparison.Ordinal);
        Assert.Contains("SystemHeader", advanced, StringComparison.Ordinal);
    }
}
