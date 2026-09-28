#if FILESMATE_UI_TEST
using System.Reflection;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyFilterPopupsAsync(SearchResultsPage page, Dictionary<string, object> report, string label)
    {
        var results = (FileDetailsSurface)page.FindName("Results");
        var resultHeight = results.ActualHeight;
        foreach (var (flyoutName, buttonName, panelName) in new[]
        {
            ("ScopeFlyout", "ScopeButton", "ScopePanel"),
            ("TypeFlyout", "TypeButton", "TypePanel"),
            ("SizeFlyout", "SizeButton", "SizePanel"),
            ("DateFlyout", "DateButton", "DatePanel"),
            ("FilterFlyout", "AdvancedButton", "AdvancedPanel"),
        })
        {
            var flyout = (Flyout)page.FindName(flyoutName);
            flyout.ShowAt((Button)page.FindName(buttonName));
            await Task.Delay(160);
            var panel = (StackPanel)page.FindName(panelName);
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
                .SelectMany(p => PolishDescendants(p.Child)).OfType<FlyoutPresenter>().Single();
            if (presenter.CornerRadius.TopLeft < 12 || presenter.CornerRadius.BottomRight < 12)
                throw new InvalidOperationException(flyoutName + " has square corners");
            if (presenter.ActualWidth > 382 || presenter.ActualHeight > 400 || Math.Abs(results.ActualHeight - resultHeight) > 1)
                throw new InvalidOperationException(flyoutName + " is too large or reduces the results viewport");
            foreach (var button in PolishDescendants(panel).OfType<Button>())
            {
                if (button.Visibility != Visibility.Visible || button.ActualWidth == 0) continue;
                if (button.Content is string && PolishDescendants(button).OfType<TextBlock>().Any(t => t.IsTextTrimmed))
                    throw new InvalidOperationException(flyoutName + " clips a button label: " + button.Content);
            }
            if (flyoutName == "TypeFlyout")
            {
                var formats = (Grid)page.FindName("FormatChoices");
                foreach (var chip in formats.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>())
                {
                    var bounds = chip.TransformToVisual(formats).TransformBounds(new Rect(0, 0, chip.ActualWidth, chip.ActualHeight));
                    if (bounds.Bottom > formats.ActualHeight + 1 || bounds.Right > formats.ActualWidth + 1)
                        throw new InvalidOperationException("Format chips are clipped by their viewport");
                }
            }
            // Native popup presenters live outside the RenderTargetBitmap tree.
            // Verify their live geometry above, then capture the content with its backdrop.
            await CapturePopupAsync(panel, "search-page-" + flyoutName + "-" + label + ".png");
            var closed = new TaskCompletionSource();
            void OnClosed(object? sender, object args)
            {
                flyout.Closed -= OnClosed;
                closed.TrySetResult();
            }
            flyout.Closed += OnClosed;
            flyout.Hide();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        report["CompactRoundedFilterPopups" + label] = true;
    }

    private async Task VerifySearchInteractionsAsync(SearchResultsPage page, Dictionary<string, object> report)
    {
        var surface = (FileDetailsSurface)page.FindName("Results");
        var path = page.ResultRows[0].Path;
        surface.TrySelectByPath(path); await Task.Delay(100);
        Require(surface.SelectedPaths().SequenceEqual([path]), "Search selection failed");
        var row = FindDescendant<FileRow>(surface, r => r.Entry.Name == Path.GetFileName(path))!;
        Require(row is not null, "No realized search row");
        var origin = row!.TransformToVisual(Content).TransformPoint(new Point(110, row.ActualHeight / 2));
        var hits = VisualTreeHelper.FindElementsInHostCoordinates(origin, Content);
        Require(hits.Any(element => ReferenceEquals(element, row)), "An overlay blocked pointer input to the search row");
        var type = typeof(FileDetailsSurface);
        type.GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [false, new Point(100, 60), false]);
        await Task.Delay(120);
        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot);
        Require(popups.Count > 0, "Search context menu did not open");
        var actions = popups.SelectMany(p => PolishDescendants(p.Child)).OfType<Button>().Where(b => b.IsEnabled).ToArray();
        Require(actions.Length >= 4, "Search context menu did not expose the shared file operations");
        foreach (var popup in popups) popup.IsOpen = false;
        report["SelectableHitTestableContextMenu" + page.ActualTheme] = true;
        var indicator = FindDescendant<Border>((TabViewItem)Tabs.SelectedItem, b => b.Name == "ActiveIndicator")!;
        var tab = (TabViewItem)Tabs.SelectedItem;
        var center = indicator.TransformToVisual(tab).TransformPoint(new Point(indicator.ActualWidth / 2, 0));
        Require(Math.Abs(center.X - tab.ActualWidth / 2) < 2, "Selected-tab underline is off center");
        report["CenteredTabIndicator"] = true;
        var sort = (ComboBox)page.FindName("SortBox");
        sort.IsDropDownOpen = true; await Task.Delay(120);
        var option = (ComboBoxItem)sort.ContainerFromIndex(1);
        Require(VisualStateManager.GoToState(option, "PointerOver", false), "Sort item has no hover state");
        await Task.Delay(60);
        var hover = FindDescendant<Grid>(option, g => g.Name == "LayoutRoot");
        Require(hover?.Background is SolidColorBrush color && color.Color.A >= 20, "Sort hover has no visible fill");
        report["SortHover" + page.ActualTheme] = true;
        sort.IsDropDownOpen = false;

        var toolbar = (Controls.Toolbar.AdaptiveCommandToolbar)page.FindName("Commands");
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer((Button)toolbar.ShelfAnchor)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        await Task.Delay(140);
        var shelf = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p => PolishDescendants(p.Child)).OfType<FileShelfPanel>().First();
        var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p => PolishDescendants(p.Child)).OfType<FlyoutPresenter>().First();
        Require(presenter.CornerRadius.TopLeft >= 12, "Shelf popup is not rounded");
        await CapturePopupAsync(shelf, "search-page-shelf-" + page.ActualTheme + ".png");
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)) popup.IsOpen = false;
        report["RoundedShelf" + page.ActualTheme] = true;
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
    private async Task VerifySearchSettingsContrastAsync(Dictionary<string, object> report)
    {
        OpenSettings("search");
        SearchSettingsPage? settings = null;
        for (var i = 0; i < 100; i++)
        {
            settings = FindDescendant<SearchSettingsPage>(Content, _ => true);
            if (settings?.IsLoaded == true) break;
            await Task.Delay(50);
        }
        if (settings is null) throw new InvalidOperationException("Search settings were not loaded");
        foreach (var theme in new[] { AppThemeKind.Dark, AppThemeKind.Light, AppThemeKind.Dark, AppThemeKind.Light })
        {
            await App.AppearanceViewModel!.SetThemeAsync(theme); await Task.Delay(180);
            var toggle = (ToggleSwitch)settings.FindName("GlobalSearchResident");
            var label = FindDescendant<TextBlock>(toggle, text => text.Text == Loc.Get("Search_Residency"));
            if (label?.Foreground is not SolidColorBrush brush || (theme == AppThemeKind.Light ? brush.Color.R > 110 : brush.Color.R < 180))
                throw new InvalidOperationException("ToggleSwitch header retained the wrong theme ink: " + theme);
            report["SettingsHeaderInk" + theme] = brush.Color.ToString();
        }
        await Capture(Content, "search-page-settings-Light.png");
        CloseSettings();
    }
    private static IEnumerable<DependencyObject> PolishDescendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in PolishDescendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
#endif
