#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Models;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunShellTransparencySmokeAsync()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "shell-transparency-smoke.json");
        var original = App.AppearanceViewModel!.Current;
        var results = new List<object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1900, 1300));
            await Task.Delay(1000);
            var root = (Grid)Content;
            string[] keys = ["FilesMate.Chrome.FillBrush", "FilesMate.Sidebar.BackgroundBrush",
                "FilesMate.CommandBar.BackgroundBrush", "FilesMate.FileArea.BackgroundBrush",
                "FilesMate.Favorites.BackgroundBrush", "FilesMate.FileContent.BackgroundBrush"];
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            foreach (var backdrop in Enum.GetValues<BackdropKind>())
            foreach (var effect in Enum.GetValues<GlassEffectMode>())
            {
                var settings = original with { Theme = theme, Backdrop = backdrop, GlassEffect = effect, TransparencyPercent = 25 };
                double[]? first = null;
                Windows.UI.Color[]? firstColors = null;
                foreach (var style in new[] { ShellStyleKind.Layered, ShellStyleKind.Unified, ShellStyleKind.Layered })
                {
                    ApplyAppearance(settings with { ShellStyle = style });
                    await Task.Delay(30);
                    var brushes = keys.Select(key => (SolidColorBrush)FindTheme(Application.Current.Resources,
                        theme == AppThemeKind.Light ? "Light" : "Dark", key)!).ToArray();
                    var coverage = brushes.Select(b => b.Opacity * b.Color.A / 255d).ToArray();
                    if (style == ShellStyleKind.Layered)
                    {
                        var opaque = backdrop == BackdropKind.Solid || effect == GlassEffectMode.Off;
                        if (coverage.Where((a, i) => Math.Abs(a - (opaque ? 1 : Coverage(i, .75))) > 0.000001).Any())
                            throw new InvalidOperationException($"Unexpected coverage: {theme}/{backdrop}/{effect}");
                        if (first is not null && !first.SequenceEqual(coverage))
                            throw new InvalidOperationException("Style round trip changed transparency.");
                        var colors = brushes.Select(b => b.Color).ToArray();
                        if (firstColors is not null && !firstColors.SequenceEqual(colors))
                            throw new InvalidOperationException("Style round trip accumulated tint compensation.");
                        first = coverage;
                        firstColors = colors;
                    }
                    results.Add(new { theme, backdrop, effect, style, coverage });
                }
            }

            // Rendered palette differences must survive even maximum transparency.
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                var name = theme == AppThemeKind.Light ? "Light" : "Dark";
                var settings = original with { Theme = theme, Backdrop = BackdropKind.Acrylic,
                    GlassEffect = GlassEffectMode.Balanced, ShellStyle = ShellStyleKind.Layered };
                Windows.UI.Color[]? palette = null;
                foreach (var percent in new[] { 0, 1, 5, 25, 50, 100 })
                {
                    ApplyAppearance(settings with { TransparencyPercent = percent });
                    var brushes = keys.Select(key => (SolidColorBrush)FindTheme(Application.Current.Resources, name, key)!).ToArray();
                    palette ??= brushes.Select(b => b.Color).ToArray();
                    if (brushes.Where((b, i) => Math.Abs(b.Opacity - Coverage(i, 1 - percent / 100d)) > .000001).Any())
                        throw new InvalidOperationException("Unexpected surface coverage.");
                    // Compare composited contrast, not raw tint: tint compensates for alpha.
                    var sidebar = brushes[1];
                    var content = brushes[^1];
                    foreach (var background in new[] { 0d, 64d, 128d, 255d })
                    {
                        var difference = content.Opacity * content.Color.R + (1 - content.Opacity) * background
                            - sidebar.Opacity * sidebar.Color.R - (1 - sidebar.Opacity) * background;
                        if (Math.Abs(difference - (palette[^1].R - palette[1].R)) > 1)
                            throw new InvalidOperationException("Wallpaper erased the sidebar/content contrast.");
                    }
                    var menu = (AcrylicBrush)FindTheme(Application.Current.Resources, name, "FilesMate.Menu.BackgroundBrush")!;
                    var material = (AcrylicBrush)FindTheme(Application.Current.Resources, name, "FilesMate.LiquidGlass.FillBrush")!;
                    var fallback = (SolidColorBrush)FindTheme(Application.Current.Resources, name, "FilesMate.LiquidGlass.SolidFillBrush")!;
                    if (menu.TintColor != material.TintColor || menu.FallbackColor != fallback.Color
                        || menu.TintOpacity != material.TintOpacity || menu.TintOpacity < 0.18
                        || menu.AlwaysUseFallback != (percent == 0))
                        throw new InvalidOperationException("Floating materials are inconsistent or unreadable.");
                    if (percent == 0 && SystemBackdrop is not null)
                        throw new InvalidOperationException("Opaque mode still creates a desktop backdrop.");
                    if (SettingsBackdrop.HasSystemBackdrop)
                        throw new InvalidOperationException("Hidden settings retains a desktop backdrop.");
                    results.Add(new { theme, percent, Coverage = brushes[0].Opacity, menu.TintOpacity });
                }
            }

            // Exercise the actual slider, its live preview, delayed persistence and unload flush.
            await App.AppearanceViewModel.SetBackdropAsync(BackdropKind.Acrylic);
            await App.AppearanceViewModel.SetGlassEffectAsync(GlassEffectMode.Balanced);
            OpenSettings("appearance");
            Slider? slider = null;
            for (var attempt = 0; attempt < 100 && slider is null; attempt++)
            {
                await Task.Delay(100);
                root.UpdateLayout();
                slider = FindDescendant<Slider>(root, item => item.Name == "TransparencySlider");
            }
            if (slider is null) throw new InvalidOperationException("Appearance page did not load.");
            var label = FindDescendant<TextBlock>(root, item => item.Name == "TransparencyValue")!;
            foreach (var percent in new[] { 0, 1, 50, 100, 65 })
            {
                slider.Value = percent;
                if (label.Text != $"{percent}%") throw new InvalidOperationException("Percentage label is stale.");
                var brush = (SolidColorBrush)FindTheme(Application.Current.Resources, "Light", "FilesMate.FileContent.BackgroundBrush")!;
                if (percent == 100 && Math.Abs(brush.Opacity - 0.40) > 0.000001 || percent == 0 && brush.Opacity != 1)
                    throw new InvalidOperationException("Slider did not update backgrounds immediately.");
                await Task.Delay(400);
                if (SettingsBackdrop.HasSystemBackdrop != (percent > 0)
                    || Math.Abs(SettingsBackdrop.TintBrush.Opacity - Animations.GlassMaterialPolicy.FloatingCoverage(1 - percent / 100d)) > .000001)
                    throw new InvalidOperationException("Settings backdrop did not follow the slider.");
                var saved = new Services.AppearanceSettingsService(Program.SettingsPath(Services.AppearanceSettingsService.DefaultFilePath)).Load();
                if (saved.TransparencyPercent != percent) throw new InvalidOperationException("Slider value was not saved.");
            }
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                slider.StartBringIntoView();
                await Task.Delay(300);
                await Capture(root, $"transparency-slider-{theme}.png");
            }
            slider.Value = 73;
            CloseSettings();
            var saveWait = System.Diagnostics.Stopwatch.StartNew();
            while (App.AppearanceViewModel.Current.TransparencyPercent != 73 && saveWait.ElapsedMilliseconds < 2000)
                await Task.Delay(25);
            if (App.AppearanceViewModel.Current.TransparencyPercent != 73)
                throw new InvalidOperationException("Closing settings lost the last slider value.");

            // Measure the live control, including the previously empty ScrollViewer row.
            var shelf = new FileShelfPanel(root, () => { })
            {
                Width = 360, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            root.Children.Add(shelf);
            await Task.Delay(100);
            root.UpdateLayout();
            var clear = (Button)shelf.FindName("ClearButton");
            var statusHost = (ScrollViewer)shelf.FindName("StatusHost");
            var buttonBounds = clear.TransformToVisual(shelf).TransformBounds(new Rect(0, 0, clear.ActualWidth, clear.ActualHeight));
            var bottomGap = shelf.ActualHeight - buttonBounds.Bottom;
            if (statusHost.ActualHeight != 0 || bottomGap > 12 || bottomGap < 8)
                throw new InvalidOperationException($"Shelf footer gap: {bottomGap}, status height: {statusHost.ActualHeight}");
            var compactHeight = shelf.ActualHeight;
            typeof(FileShelfPanel).GetMethod("SetStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(shelf, ["Transfer status remains visible when needed."]);
            root.UpdateLayout();
            if (statusHost.ActualHeight <= 0 || shelf.ActualHeight <= compactHeight)
                throw new InvalidOperationException("Shelf status failed to expand.");
            root.Children.Remove(shelf);
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, Cases = results.Count,
                ShelfHeight = compactHeight, ShelfBottomGap = bottomGap, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() }));
        }
        finally
        {
            ApplyAppearance(original);
        }

        static double Coverage(int index, double opacity) => index switch
        {
            0 => Animations.GlassMaterialPolicy.FloatingCoverage(opacity),
            3 => Animations.GlassMaterialPolicy.FoundationCoverage(opacity),
            _ => Animations.GlassMaterialPolicy.LayerCoverage(opacity),
        };

        static object? FindTheme(ResourceDictionary resources, string themeName, string key)
        {
            if (resources.ThemeDictionaries.TryGetValue(themeName, out var value)
                && value is ResourceDictionary theme && theme.TryGetValue(key, out var brush)) return brush;
            foreach (var merged in resources.MergedDictionaries)
                if (FindTheme(merged, themeName, key) is { } match) return match;
            return null;
        }
    }
}
#endif
