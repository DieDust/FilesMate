using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Operations;

public sealed class FileLockDialogContractTests
{
    [Fact]
    public void Lock_dialog_uses_a_selectable_process_tree()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "FileLockDialog.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "FileLockDialog.xaml.cs"));
        var rowXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "FileLockNodeView.xaml"));
        var row = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "FileLockNodeView.xaml.cs"));
        Assert.DoesNotContain("FilesMate.LiquidGlassSurfaceStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("ProcessList", xaml, StringComparison.Ordinal);
        Assert.Contains("FileLockNodeView", xaml, StringComparison.Ordinal);
        Assert.Contains("UnlockButton", xaml, StringComparison.Ordinal);
        Assert.Contains("DeleteButton", xaml, StringComparison.Ordinal);
        Assert.Contains("OtherButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TreeView", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("Expand_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSelected", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem = nodes[0]", code, StringComparison.Ordinal);
        Assert.Contains("ReleasePreviewAsync", code, StringComparison.Ordinal);
        Assert.Contains("TargetsToDelete", code, StringComparison.Ordinal);
        Assert.Contains("FileLockQuery.HandlesFor", code, StringComparison.Ordinal);
        Assert.Contains("FileLockQuery.Find", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FileLockQuery.Unlock", code, StringComparison.Ordinal);
        Assert.Contains("FileLockQuery.Terminate", code, StringComparison.Ordinal);
        Assert.Contains("ShowError", code, StringComparison.Ordinal);
        Assert.Contains("ShellIconBinder.BindPath", row, StringComparison.Ordinal);
        Assert.Contains("PointerPressed", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Tapped +=", row, StringComparison.Ordinal);
        Assert.DoesNotContain("LockHunter", code, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PathText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PathBox.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("FocusState.Programmatic", code, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Spacing=\"4\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,2\"", rowXaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"8\"", rowXaml, StringComparison.Ordinal);
        Assert.Contains("ContentDialogTheme.Apply", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Tags", "TagEditor.cs")), StringComparison.Ordinal);
        Assert.Contains("ShowLockOverlayAsync", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LockOverlay\"", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml")), StringComparison.Ordinal);
        Assert.Contains("VacateFoldersAsync", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("WhenFolderReleased", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Navigation", "PaneViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("RestartManagerFiles", File.ReadAllText(Path.Combine(ThemeXaml.RepoRoot, "src", "FilesMate.Platform.Windows", "Locks", "FileLockQuery.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseOwn", File.ReadAllText(Path.Combine(ThemeXaml.RepoRoot, "src", "FilesMate.Platform.Windows", "Locks", "FileLockQuery.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("group.Key is 0 or 4 || group.Key == Environment.ProcessId", File.ReadAllText(Path.Combine(ThemeXaml.RepoRoot, "src", "FilesMate.Platform.Windows", "Locks", "FileLockQuery.cs")), StringComparison.Ordinal);
        Assert.Contains("internal static ElementTheme Resolve", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Theming", "ContentDialogTheme.cs")), StringComparison.Ordinal);
        Assert.Contains("content.RequestedTheme", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("DeleteDirectoryTree", File.ReadAllText(Path.Combine(ThemeXaml.RepoRoot, "src", "FilesMate.Platform.Windows", "Operations", "WindowsLocalFileOperations.cs")), StringComparison.Ordinal);
        Assert.Contains("TryPosixDelete", File.ReadAllText(Path.Combine(ThemeXaml.RepoRoot, "src", "FilesMate.Platform.Windows", "Operations", "WindowsLocalFileOperations.cs")), StringComparison.Ordinal);
        Assert.Contains("longPathAware", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "app.manifest")), StringComparison.Ordinal);
    }
}
