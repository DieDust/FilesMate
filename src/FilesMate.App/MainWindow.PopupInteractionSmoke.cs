#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.Omnibar;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Models;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunPopupInteractionSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(2100, 1400));
            await App.AppearanceViewModel!.SetThemeAndAccentAsync(AppThemeKind.Light, AccentKind.Default);
            await App.AppearanceViewModel.SetBackdropAsync(BackdropKind.Solid);
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { HiddenToolbarTools = null });
            NavigatorPage? page = null;
            for (var i = 0; i < 200; i++)
            {
                if (TabHost.Content is NavigatorPage { IsLoaded: true } loaded && !loaded.ViewModel.IsLoading
                    && loaded.ViewModel.Navigation.CurrentPath is { Length: > 0 }) { page = loaded; break; }
                await Task.Delay(50);
            }
            Require(page is not null, "Navigator did not initialize");
            var fixture = System.IO.Path.Combine(AppContext.BaseDirectory, "test-profile", "crumb-drop-" + Guid.NewGuid().ToString("N"));
            var parent = Directory.CreateDirectory(System.IO.Path.Combine(fixture, "Parent")).FullName;
            var source = Directory.CreateDirectory(System.IO.Path.Combine(parent, "Child")).FullName;
            page!.ViewModel.Navigate(source);
            for (var i = 0; i < 200 && (page.ViewModel.Navigation.CurrentPath != source || page.ViewModel.IsLoading); i++) await Task.Delay(30);
            await Task.Delay(200);
            var omnibar = (Omnibar)page.FindName("Omni");
            report["BreadcrumbDrop"] = await omnibar.RunFileDropSmokeAsync(source, parent);

            var toolbar = new AdaptiveCommandToolbar { Width = 1000, ShowCommandLabels = true };
            var host = new Border { Child = toolbar, Padding = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top };
            TabHost.Content = host;
            await Task.Delay(200);
            toolbar.SetLayout(Controls.FileSurface.FileLayoutKind.Grid);
            toolbar.ApplyContext(Commands.CommandContext.ForToolbar(3));
            var checks = new List<object>();
            report["VisualStates"] = checks;
            // Exercise a live dark -> light transition as well as a freshly opened popup.
            foreach (var mode in new[] { AppThemeKind.Dark, AppThemeKind.Light })
            {
                await App.AppearanceViewModel.SetThemeAsync(mode); await Task.Delay(150);
                var palette = SkinPalette.For(mode == AppThemeKind.Dark);
                var view = (Button)toolbar.FindName("ViewMenuButton");
                var menu = (MenuFlyout)view.Flyout;
                menu.ShowAt(view); await Task.Delay(140);
                foreach (var item in menu.Items.OfType<ToggleMenuFlyoutItem>())
                foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
                {
                    Require(VisualStateManager.GoToState(item, state, false), "Missing menu state " + state);
                    await Task.Delay(35);
                    var text = Part<TextBlock>(item, "TextBlock");
                    var layout = Part<Grid>(item, "LayoutRoot");
                    Require(BrushColor(text.Foreground) == palette.Text, mode + " menu text changed in " + state);
                    checks.Add(new { Theme = mode.ToString(), Control = item.Text, State = state, Text = BrushColor(text.Foreground), Fill = BrushColor(layout.Background) });
                    if (state == "PointerOver" && item == menu.Items[0]) await Capture(Presenter(item), "popup-menu-hover-" + mode + ".png");
                    if (state != "Normal") Require(VisibleFill(layout.Background, palette.Floating),
                        mode + " " + item.Text + " " + state + " fill " + BrushColor(layout.Background).ToString("X8") + " on " + palette.Floating.ToString("X8"));
                    VisualStateManager.GoToState(item, "Normal", false);
                }
                var separator = menu.Items.OfType<MenuFlyoutSeparator>().Single();
                var line = PolishDescendants(separator).OfType<Rectangle>().Single();
                Require(line.ActualHeight >= 1 && VisibleFill(line.Fill, palette.Floating), "View menu separator is invisible");
                var submenu = menu.Items.OfType<MenuFlyoutSubItem>().First();
                foreach (var state in new[] { "PointerOver", "Pressed" })
                {
                    VisualStateManager.GoToState(submenu, state, false); await Task.Delay(35);
                    Require(BrushColor(Part<TextBlock>(submenu, "TextBlock").Foreground) == palette.Text, "Submenu text changed in " + state);
                    Require(VisibleFill(Part<Grid>(submenu, "LayoutRoot").Background, palette.Floating), "Submenu feedback invisible");
                }
                var expansion = (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(submenu).GetPattern(PatternInterface.ExpandCollapse);
                expansion.Expand(); await Task.Delay(220);
                Require(expansion.ExpandCollapseState == Microsoft.UI.Xaml.Automation.ExpandCollapseState.Expanded, "Submenu did not expand");
                Require(BrushColor(Part<TextBlock>(submenu, "TextBlock").Foreground) == palette.Text, "Expanded submenu text is unreadable");
                Require(VisibleFill(Part<Grid>(submenu, "LayoutRoot").Background, palette.Floating), "Expanded submenu has no feedback");
                await Capture(Presenter(submenu), "popup-submenu-open-" + mode + ".png");
                expansion.Collapse(); menu.Hide(); await Task.Delay(80);

                foreach (var name in new[] { "SortButton", "GroupingMenuButton" })
                {
                    var button = (Button)toolbar.FindName(name); var flyout = (Flyout)button.Flyout;
                    flyout.ShowAt(button); await Task.Delay(140);
                    var content = (FrameworkElement)flyout.Content;
                    if (name == "GroupingMenuButton")
                    {
                        Require(content.ActualWidth < 250, "Grouping card still too wide: " + content.ActualWidth);
                        report["GroupingWidth"] = content.ActualWidth;
                    }
                    var radios = PolishDescendants(content).OfType<RadioButton>().ToArray();
                    foreach (var radio in radios)
                    foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
                    {
                        VisualStateManager.GoToState(radio, state, false); await Task.Delay(35);
                        var outline = Part<Ellipse>(radio, radio.IsChecked == true ? "CheckOuterEllipse" : "OuterEllipse");
                        Require(Contrast(BrushColor(outline.Stroke), palette.Floating) >= 3,
                            mode + " radio outline disappeared in " + state + ": " + BrushColor(outline.Stroke).ToString("X8"));
                        checks.Add(new { Theme = mode.ToString(), Control = name, State = state, Checked = radio.IsChecked,
                            Stroke = BrushColor(outline.Stroke), Fill = BrushColor(outline.Fill) });
                        if (state == "PointerOver" && radio == radios[1]) await CapturePopupAsync(content, "popup-" + name + "-hover-" + mode + ".png");
                        VisualStateManager.GoToState(radio, "Normal", false);
                    }
                    flyout.Hide(); await Task.Delay(80);
                }
            }
            report["VisualStates"] = checks;
            report["Passed"] = true;
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            try { await Capture(Content, "popup-interaction-failure.png"); } catch { }
        }
        await File.WriteAllTextAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "popup-interaction-smoke.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static T Part<T>(DependencyObject root, string name) where T : FrameworkElement =>
            PolishDescendants(root).OfType<T>().Single(e => e.Name == name);
        static MenuFlyoutPresenter Presenter(DependencyObject child)
        {
            while (child is not MenuFlyoutPresenter) child = VisualTreeHelper.GetParent(child);
            return (MenuFlyoutPresenter)child;
        }
        static uint BrushColor(Brush? brush) => brush is SolidColorBrush { Color: var c }
            ? (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B : 0;
        static bool VisibleFill(Brush? brush, uint background) => Contrast(BrushColor(brush), background) >= 1.12;
        static double Contrast(uint foreground, uint background)
        {
            double Luminance(uint color, uint surface)
            {
                var alpha = (color >> 24) / 255d;
                double Channel(int shift)
                {
                    var value = (((color >> shift) & 255) * alpha + ((surface >> shift) & 255) * (1 - alpha)) / 255d;
                    return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                }
                return .2126 * Channel(16) + .7152 * Channel(8) + .0722 * Channel(0);
            }
            var a = Luminance(foreground, background); var b = Luminance(background, background);
            return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        }
    }
}
#endif
