using FilesMate.App.Navigation;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.FileSurface;

public sealed class FilePaneStateTests
{
    [Fact]
    public void States_are_mutually_exclusive_and_loading_waits_150ms()
    {
        var pane = new FilePanePresentation();
        pane.Apply(new FilePaneSnapshot(1, true, 0, null, null, true, TimeSpan.FromMilliseconds(80)));
        Assert.Equal(FilePaneKind.Content, pane.Kind);
        Assert.False(pane.ShowLoadingIndicator);
        Assert.False(pane.ShowInfo);

        pane.Apply(new FilePaneSnapshot(1, true, 0, null, null, true, TimeSpan.FromMilliseconds(150)));
        Assert.Equal(FilePaneKind.LoadingDelayed, pane.Kind);
        Assert.True(pane.ShowLoadingIndicator);
        Assert.False(pane.ShowInfo);

        pane.Apply(new FilePaneSnapshot(1, false, 0, null, null, true, TimeSpan.Zero));
        Assert.Equal(FilePaneKind.Empty, pane.Kind);
        Assert.True(pane.ShowInfo);
        Assert.Equal("This folder is empty", pane.Title);

        pane.Apply(new FilePaneSnapshot(2, false, 12, null, null, true, TimeSpan.Zero));
        Assert.Equal(FilePaneKind.Content, pane.Kind);
        Assert.True(pane.ShowList);
        Assert.False(pane.ShowInfo);
        Assert.Equal(2, pane.Generation);
    }

    [Fact]
    public void Errors_are_classified_without_hresult_and_offer_actions()
    {
        var pane = new FilePanePresentation();
        pane.Apply(new FilePaneSnapshot(3, false, 0, "Access is denied. HRESULT: -2147024891 0x80070005", null, true, TimeSpan.Zero));
        Assert.Equal(FilePaneKind.AccessDenied, pane.Kind);
        Assert.Equal("Retry", pane.PrimaryAction);
        Assert.Equal("Go up", pane.SecondaryAction);
        Assert.DoesNotContain("HRESULT", pane.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", pane.Body, StringComparison.OrdinalIgnoreCase);

        pane.Apply(new FilePaneSnapshot(4, false, 0, "The network path is offline.", null, true, TimeSpan.Zero));
        Assert.Equal(FilePaneKind.Offline, pane.Kind);
        Assert.Equal("Retry", pane.PrimaryAction);

        pane.Apply(new FilePaneSnapshot(5, false, 0, "Folder is missing.", null, true, TimeSpan.Zero));
        Assert.Equal(FilePaneKind.NotFound, pane.Kind);
        Assert.Equal("Go up", pane.PrimaryAction);

        pane.Apply(new FilePaneSnapshot(6, false, 0, "Something exploded.", null, false, TimeSpan.Zero));
        Assert.Equal(FilePaneKind.UnknownError, pane.Kind);
        Assert.Equal("Retry", pane.PrimaryAction);
        Assert.Null(pane.SecondaryAction);
        Assert.Equal("Something exploded.", pane.Body);
    }

    [Fact]
    public void Newer_generation_replaces_the_previous_state()
    {
        var pane = new FilePanePresentation();
        pane.Apply(new FilePaneSnapshot(8, true, 0, null, null, true, TimeSpan.FromMilliseconds(400)));
        Assert.Equal(8, pane.Generation);
        Assert.Equal(FilePaneKind.LoadingDelayed, pane.Kind);

        pane.Apply(new FilePaneSnapshot(9, false, 4, null, null, true, TimeSpan.Zero));
        Assert.Equal(9, pane.Generation);
        Assert.Equal(FilePaneKind.Content, pane.Kind);
        Assert.False(pane.ShowLoadingIndicator);
    }

    [Fact]
    public void Chrome_hosts_status_info_and_delayed_loading_controls()
    {
        var chrome = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml"));
        Assert.Contains("FileStatusBar", chrome, StringComparison.Ordinal);
        Assert.Contains("InfoStateView", chrome, StringComparison.Ordinal);
        Assert.Contains("LoadingPresenter", chrome, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BodyPresenter\"", chrome, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageHost", chrome, StringComparison.Ordinal);

        var info = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "InfoStates", "InfoStateView.xaml"));
        Assert.Contains("PrimaryAction", info, StringComparison.Ordinal);
        Assert.Contains("SecondaryAction", info, StringComparison.Ordinal);

        var status = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Status", "FileStatusBar.xaml"));
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", status, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Control.Height.StatusBar", status, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SizeBlock\"", status, StringComparison.Ordinal);

        var delay = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml.cs"));
        Assert.Contains("LoadingDelayMilliseconds", delay, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigate(", delay, StringComparison.Ordinal);
    }
}
