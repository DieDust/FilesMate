using FilesMate.App.Models;

namespace FilesMate.App.Tests.Settings;

public sealed class WindowPlacementTests
{
    [Fact]
    public void Fit_centers_an_unset_window_inside_the_work_area()
    {
        var fitted = WindowPlacementMath.Fit(
            WindowPlacement.Default,
            workX: 0,
            workY: 0,
            workWidth: 1920,
            workHeight: 1080,
            minWidth: 1024,
            minHeight: 640);

        Assert.Equal(1440, fitted.Width);
        Assert.Equal(900, fitted.Height);
        Assert.Equal(240, fitted.X);
        Assert.Equal(90, fitted.Y);
        Assert.False(fitted.Maximized);
    }

    [Fact]
    public void Fit_clamps_an_off_screen_window_back_onto_the_display()
    {
        var fitted = WindowPlacementMath.Fit(
            new WindowPlacement(8000, -400, 1600, 1000, false),
            workX: 0,
            workY: 0,
            workWidth: 1920,
            workHeight: 1080,
            minWidth: 1024,
            minHeight: 640);

        Assert.Equal(1600, fitted.Width);
        Assert.Equal(1000, fitted.Height);
        Assert.InRange(fitted.X, 0, 320);
        Assert.InRange(fitted.Y, 0, 80);
    }

    [Fact]
    public void Service_round_trips_bounds_through_window_json()
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"), "window.json");
        try
        {
            var service = new FilesMate.App.Services.WindowPlacementService(path);
            service.Save(new WindowPlacement(40, 50, 1280, 800, true));
            var loaded = service.Load();
            Assert.Equal(40, loaded.X);
            Assert.Equal(50, loaded.Y);
            Assert.Equal(1280, loaded.Width);
            Assert.Equal(800, loaded.Height);
            Assert.True(loaded.Maximized);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Main_window_restores_bounds_before_first_activation()
    {
        var path = Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.AppRoot,
            "MainWindow.xaml.cs");
        var source = File.ReadAllText(path);
        var restore = source.IndexOf("RestorePlacement();", StringComparison.Ordinal);
        var changed = source.IndexOf("AppWindow.Changed += AppWindow_Changed", StringComparison.Ordinal);
        var activated = source.IndexOf("private void OnWindowActivated()", StringComparison.Ordinal);
        Assert.InRange(restore, 0, changed - 1);
        Assert.True(changed < activated);
        var activationBody = source[activated..source.IndexOf("public void SetFolderTab", activated, StringComparison.Ordinal)];
        Assert.DoesNotContain("RestorePlacement();", activationBody, StringComparison.Ordinal);
    }
}
