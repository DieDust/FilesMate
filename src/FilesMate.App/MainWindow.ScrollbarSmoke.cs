#if FILESMATE_UI_TEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using FilesMate.App.Models;
using System.Text.Json;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunScrollbarSmokeAsync()
    {
        AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1100));
        await Task.Delay(1400);
        var rows = new List<object>();
        foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark, AppThemeKind.Light })
        {
            await App.AppearanceViewModel!.SetThemeAsync(theme);
            foreach (var area in new[] { "sidebar", "settings" })
            {
                if (area == "settings") OpenSettings("appearance");
                var sidebar = FindDescendant<ScrollViewer>((DependencyObject)Content, s => s.Name == "PlacesScroll")!;
                sidebar.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                await Task.Delay(500);
                var bars = Descendants((DependencyObject)Content).OfType<ScrollBar>().Where(b => b.ActualHeight > 50 || b.ActualWidth > 50).ToArray();
                foreach (var state in new[] { "Collapsed", "Expanded", "PointerOver", "Pressed" })
                {
                    foreach (var bar in bars)
                    {
                        VisualStateManager.GoToState(bar, state is "PointerOver" or "Pressed" ? state : "Normal", false);
                        VisualStateManager.GoToState(bar, "MouseIndicator", false);
                        VisualStateManager.GoToState(bar, state == "Collapsed" ? "Collapsed" : "Expanded", false);
                    }
                    await Task.Delay(700);
                    foreach (var bar in bars)
                    {
                        var thumb = Descendants(bar).OfType<Thumb>().FirstOrDefault(t => t.Name == (bar.Orientation == Orientation.Vertical ? "VerticalThumb" : "HorizontalThumb"));
                        var track = Descendants(bar).OfType<Rectangle>().FirstOrDefault(t => t.Name.EndsWith("TrackRect") && t.ActualHeight > 0);
                        rows.Add(new { Theme = theme.ToString(), Area = area, State = state, BarTheme = bar.ActualTheme.ToString(), bar.Name,
                            Thumb = (thumb?.Background as SolidColorBrush)?.Color.ToString(), Track = (track?.Fill as SolidColorBrush)?.Color.ToString(), TrackType = track?.Fill?.GetType().Name, TrackOpacity = track?.Opacity });
                    }
                    await Capture((UIElement)Content, $"scrollbar-{theme}-{area}-{state}.png");
                }
                if (area == "settings") CloseSettings();
            }
        }
        File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "scrollbar-results.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
#endif
