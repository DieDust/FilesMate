#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunBrowsingPolishSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(2100, 1500));
            await App.AppearanceViewModel!.SetThemeAndAccentAsync(AppThemeKind.Light, AccentKind.Default);
            await App.AppearanceViewModel.SetBackdropAsync(BackdropKind.Solid);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { HiddenToolbarTools = null,
                FileOpeningMode = ItemOpeningMode.DoubleClick, FolderOpeningMode = ItemOpeningMode.DoubleClick, ShowAlphabetNavigation = false });
            for (var i = 0; i < 100 && TabHost.Content is not NavigatorPage { IsLoaded: true }; i++) await Task.Delay(50);
            var surface = new FileDetailsSurface { Width = 900, Height = 480 };
            var toolbar = new AdaptiveCommandToolbar { Width = 900, ShowCommandLabels = true };
            var stack = new StackPanel { Width = 900, Spacing = 6 };
            stack.Children.Add(toolbar); stack.Children.Add(surface);
            var host = new Border { Child = stack, Padding = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            Theming.ThemeResources.Bind(host, Border.BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
            TabHost.Content = host; await Task.Delay(200);
            toolbar.ApplyContext(CommandContext.ForToolbar(3));
            toolbar.LayoutChanged += (_, kind) => { surface.SetLayout(kind); toolbar.SetLayout(kind); };
            toolbar.GridSizeRequested += (_, size) => { surface.SetGridSize(size); toolbar.SetViewSize(size, surface.ListZoomPercent); };
            await surface.RunMarqueeSmokeAsync();
            using (var marquee = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "marquee-results.json"))))
            {
                report["ScrollingMarquee"] = marquee.RootElement.Clone();
                Require(marquee.RootElement.GetProperty("Passed").GetBoolean(), "Scrolling marquee regression failed");
            }
            report["GapSelection"] = await surface.RunGapSelectionSmokeAsync();
            toolbar.SetLayout(FileLayoutKind.Grid); toolbar.SetViewSize(GridSizePreset.Large, 100);
            await Task.Delay(160);
            Require(!PolishDescendants(surface).OfType<CheckBox>().Any(), "File checkboxes remain");
            await Capture(host, "browsing-grid-light.png");

            var viewButton = (Button)toolbar.FindName("ViewMenuButton");
            var menu = (MenuFlyout)viewButton.Flyout;
            var primary = menu.Items.OfType<ToggleMenuFlyoutItem>().ToArray();
            Require(primary.Length == 3 && menu.Items.OfType<MenuFlyoutSubItem>().First().Items.Count == 7, "View choices missing");
            foreach (var (key, layout) in new[] { ("Layout_List", FileLayoutKind.List), ("Layout_Details", FileLayoutKind.Details), ("Layout_LargeIcons", FileLayoutKind.Grid) })
            {
                menu.ShowAt(viewButton); await Task.Delay(80);
                Invoke(primary.Single(item => item.Text == StringTable.Get(key)));
                await Task.Delay(120);
                Require(surface.LayoutKind == layout, "View selection failed: " + key);
                if (layout == FileLayoutKind.List) await Capture(host, "browsing-list-light.png");
            }
            menu.ShowAt(viewButton); await Task.Delay(120);
            await Capture(VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).Select(p => p.Child).OfType<MenuFlyoutPresenter>().Last(), "browsing-view-menu.png");
            menu.Hide();
            foreach (var width in new[] { 360d, 600d, 900d })
            {
                toolbar.Width = width; await Task.Delay(160);
                Require(toolbar.FindName("DetailsViewButton") is null && toolbar.FindName("GridViewButton") is null && toolbar.FindName("ListViewButton") is null, "Split view controls remain");
                Require(toolbar.FindName("ViewSwitchGroup") is null, "View background wrapper remains");
            }
            toolbar.Width = 900;
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(12) };
            foreach (var mode in new[] { EntryGrouping.FoldersFirst, EntryGrouping.Mixed, EntryGrouping.FilesFirst })
            {
                var icon = new EntryGroupingIcon { Width = 24, Height = 24 }; icon.SetGrouping(mode);
                var sample = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                sample.Children.Add(icon); sample.Children.Add(new TextBlock { Text = StringTable.Get("Sort_" + mode), VerticalAlignment = VerticalAlignment.Center });
                icons.Children.Add(sample);
            }
            var iconHost = new Border { Child = icons, HorizontalAlignment = HorizontalAlignment.Left };
            Theming.ThemeResources.Bind(iconHost, Border.BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
            stack.Children.Insert(0, iconHost); await Task.Delay(120);
            await Capture(iconHost, "browsing-order-icons.png"); stack.Children.Remove(iconHost);
            var groupingButton = (Button)toolbar.FindName("GroupingMenuButton");
            var grouping = (Flyout)groupingButton.Flyout;
            grouping.ShowAt(groupingButton); await Task.Delay(100);
            foreach (var radio in PolishDescendants(grouping.Content).OfType<RadioButton>()) radio.IsChecked = true;
            await CapturePopupAsync((FrameworkElement)grouping.Content, "browsing-order-menu.png"); grouping.Hide();
            report["SingleViewButtonAndMenus"] = true;
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark); await Task.Delay(140);
            await Capture(host, "browsing-grid-dark.png");
            surface.ReleaseResources();

            var appearance = new AppearancePage();
            var scroll = new ScrollViewer { Content = appearance, Width = 720, Height = 650 };
            TabHost.Content = scroll;
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light); await Task.Delay(200);
            var card = (FrameworkElement)appearance.FindName("TypographyCard");
            var fields = (Grid)appearance.FindName("TypographyFields");
            var sizes = (Grid)appearance.FindName("TypographySizes");
            card.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }); await Task.Delay(180);
            Require(Grid.GetColumn(sizes) == 1 && Grid.GetRow(sizes) == 0, "Wide typography is not two columns");
            Require(card.ActualHeight < 280, "Typography card remains oversized");
            report["TypographyCardHeight"] = card.ActualHeight;
            await Capture(card, "browsing-fonts-wide.png");
            var family = (ComboBox)appearance.FindName("FileFontBox");
            for (var i = 0; i < 100 && family.Items.Count < 3; i++) await Task.Delay(30);
            family.SelectedItem = "Microsoft YaHei UI";
            ((ComboBox)appearance.FindName("FileNameSizeBox")).SelectedItem = 18;
            ((ComboBox)appearance.FindName("FileDetailsSizeBox")).SelectedItem = 14;
            await Task.Delay(250);
            Require(App.AppearanceViewModel.Current.FileNameFontSize == 18 && App.AppearanceViewModel.Current.FileDetailsFontSize == 14, "Font size controls did not apply");
            Require(((TextBlock)appearance.FindName("FileFontPreview")).FontSize == 18, "Font preview did not update");
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            scroll.Width = 380; await Task.Delay(180);
            Require(Grid.GetColumn(sizes) == 0 && Grid.GetRow(sizes) == 1, "Narrow typography did not stack");
            Require(fields.ActualWidth <= scroll.Width, "Typography overflows narrow pane");
            card.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }); await Task.Delay(120);
            await Capture(card, "browsing-fonts-narrow.png");
            report["TypographyTwoColumnsResponsiveAndLive"] = true;
            report["Passed"] = true;
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            try { await Capture(Content, "browsing-polish-failure.png"); } catch { }
        }
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "browsing-polish-smoke.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
        static void Invoke(MenuFlyoutItem item) => ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
    }
}
#endif
