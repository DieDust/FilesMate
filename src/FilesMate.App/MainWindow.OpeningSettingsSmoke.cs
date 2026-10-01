#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Home;
using FilesMate.App.Controls.Status;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunOpeningSettingsSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1440, 1060));
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await Until(() => TabHost.Content is NavigatorPage page && page.IsLoaded && !page.ViewModel.IsLoading);
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "opening-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            var folder = Directory.CreateDirectory(Path.Combine(fixture, "项目资料")).FullName;
            var file = Path.Combine(fixture, "Report.txt");
            File.WriteAllText(file, "Opening behavior fixture");
            var store = new EntryStore();
            store.Append([new(0, Path.GetFileName(folder), 0, 0, 0, FileAttributes.Directory, EntryKind.Directory),
                new(1, Path.GetFileName(file), 5, 0, 0, FileAttributes.Normal, EntryKind.File)]);
            var surface = new FileDetailsSurface { ResolvePath = entry => Path.Combine(fixture, entry.Name) };
            var previous = TabHost.Content;
            TabHost.Content = surface;
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 1), 1);
            await Task.Delay(250);
            foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List, FileLayoutKind.Grid })
            {
                surface.SetLayout(layout);
                surface.SetGridSize(GridSizePreset.Large);
                foreach (var files in Enum.GetValues<ItemOpeningMode>())
                foreach (var folders in Enum.GetValues<ItemOpeningMode>())
                {
                    await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { FileOpeningMode = files, FolderOpeningMode = folders });
                    await Task.Delay(120);
                    await Until(() => PolishDescendants(surface).OfType<FrameworkElement>().Count(element =>
                        element is FileRow { EntryId: >= 0 } || element is FileTile { EntryId: >= 0 }) >= 2);
                    foreach (var id in new[] { 0, 1 })
                    {
                        var item = PolishDescendants(surface).OfType<FrameworkElement>().First(element =>
                            element is FileRow row && row.EntryId == id || element is FileTile tile && tile.EntryId == id);
                        var name = (TextBlock)item.FindName("NameText");
                        var icon = (FrameworkElement)item.FindName("IconImage");
                        var checkbox = item.FindName("SelectionBox") as CheckBox;
                        Require(checkbox is null || checkbox.Visibility == Visibility.Collapsed,
                            $"{layout}/{files}/{folders}/{id}: selection affordance");
                        Require(!Bounds(name, item).Contains(Center(icon, item)), "Name activation area covers the icon.");
                        Require(name.ActualWidth > 0 && name.ActualWidth < item.ActualWidth - 12, "Name activation expands over the entire row/tile.");
                    }
                }
            }
            report["LivePreferencesAllNineCombinationsListAndGridHitBounds"] = true;
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with
            {
                FileOpeningMode = ItemOpeningMode.SingleClick,
                FolderOpeningMode = ItemOpeningMode.SingleClick
            });
            surface.Width = Math.Min(800, TabHost.ActualWidth);
            surface.Height = 210;
            TabHost.Content = null;
            var sampleHost = new Border { Width = surface.Width, Height = 210, Child = surface };
            Theming.ThemeResources.Bind(sampleHost, Border.BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
            TabHost.Content = sampleHost;
            surface.TrySelectByName("项目资料");
            var selectionSamples = new List<object>();
            report["SelectionAppearance"] = selectionSamples;
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List, FileLayoutKind.Grid })
                {
                    surface.SetLayout(layout);
                    await Task.Delay(180);
                    await Until(() => surface.IsLoaded && surface.ActualHeight > 0 && PolishDescendants(surface).OfType<FrameworkElement>()
                        .Any(element => element is FileRow { EntryId: 0 } || element is FileTile { EntryId: 0 }));
                    var item = PolishDescendants(surface).OfType<FrameworkElement>().First(element =>
                        element is FileRow { EntryId: 0 } || element is FileTile { EntryId: 0 });
                    var checkbox = item.FindName("SelectionBox") as CheckBox;
                    var mark = checkbox is null ? null : PolishDescendants(checkbox).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>()
                        .SingleOrDefault(rectangle => rectangle.Name == "NormalRectangle");
                    selectionSamples.Add(new
                    {
                        Layout = layout.ToString(), Theme = theme.ToString(),
                        HitWidth = checkbox?.ActualWidth, HitHeight = checkbox?.ActualHeight,
                        MarkWidth = mark?.ActualWidth, MarkHeight = mark?.ActualHeight, RadiusX = mark?.RadiusX,
                        AccentVisible = item.FindName("AccentBar") is FrameworkElement { Visibility: Visibility.Visible },
                        IsChecked = checkbox?.IsChecked
                    });
                    await Capture(sampleHost, $"file-selection-{layout}-{theme}.png");
                }
            }
            surface.ReleaseResources();
            TabHost.Content = previous;
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { FileOpeningMode = ItemOpeningMode.DoubleClick, FolderOpeningMode = ItemOpeningMode.NameClick });
            OpenSettings("files-folders");
            await Until(() => _settingsPage?.IsLoaded == true && PolishDescendants(_settingsPage).OfType<ComboBox>().Any(box => box.Name == "FileOpeningModeBox"));
            var settings = _settingsPage!;
            var fileMode = PolishDescendants(settings).OfType<ComboBox>().Single(box => box.Name == "FileOpeningModeBox");
            var folderMode = PolishDescendants(settings).OfType<ComboBox>().Single(box => box.Name == "FolderOpeningModeBox");
            Require(fileMode.SelectedIndex == 0 && folderMode.SelectedIndex == 2 && fileMode.Items.Count == 3 && folderMode.Items.Count == 3, "Independent dropdown choices failed.");
            fileMode.SelectedIndex = 1;
            await Until(() => App.ExplorerPreferences.FileOpeningMode == ItemOpeningMode.SingleClick);
            Require(App.ExplorerPreferences.FolderOpeningMode == ItemOpeningMode.NameClick, "File option changed folder preference.");
            folderMode.SelectedIndex = 0;
            await Until(() => App.ExplorerPreferences.FolderOpeningMode == ItemOpeningMode.DoubleClick);
            Require(App.ExplorerPreferences.FileOpeningMode == ItemOpeningMode.SingleClick, "Folder option changed file preference.");
            var navigate = typeof(SettingsPage).GetMethod("NavigateToSetting", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var query in new[] { "隐藏", "双击", "语言" })
            {
                var entry = SettingsSearchCatalog.Search(query).First();
                navigate.Invoke(settings, [entry]);
                await Task.Delay(550);
                var section = (FrameworkElement)((ContentPresenter)settings.FindName("SectionHost")).Content;
                var target = (FrameworkElement)section.FindName(entry.TargetName);
                var scroller = (ScrollViewer)settings.FindName("SectionScroller");
                var bounds = Bounds(target, scroller);
                Require(bounds.Bottom > 0 && bounds.Top < scroller.ActualHeight, "Search target was not scrolled into view: " + entry.TitleKey);
            }
            navigate.Invoke(settings, [SettingsSearchCatalog.Entries.Single(entry => entry.TitleKey == "Opening_FileTitle")]);
            await Task.Delay(400);
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Task.Delay(200);
                await Capture(Content, $"opening-settings-{theme}.png");
            }
            Require(!TryLightDismissSettings(settings), "Clicking settings content closes it.");
            fileMode.IsDropDownOpen = true;
            await Task.Delay(200);
            Require(!TryLightDismissSettings(SettingsOverlay), "Open dropdown was treated as background.");
            fileMode.IsDropDownOpen = false;
            await Task.Delay(200);
            Require(TryLightDismissSettings(SettingsOverlay) && SettingsOverlay.Visibility == Visibility.Collapsed, "Outside settings did not dismiss it.");
            report["SettingsSearchTargetsDropdownPersistenceAndDismissGuards"] = true;
            AddNavigatorTab(fixture);
            await Until(() => TabHost.Content is NavigatorPage p && p.IsLoaded && !p.ViewModel.IsLoading && p.ViewModel.AddressText == fixture
                && ((FileDetailsSurface)p.FindName("FileSurface")).ActualHeight > 0);
            var navigator = (NavigatorPage)TabHost.Content;
            var navigatorSurface = (FileDetailsSurface)navigator.FindName("FileSurface");
            navigatorSurface.SetLayout(FileLayoutKind.Details);
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Task.Delay(180);
                typeof(FileDetailsSurface).GetMethod("ShowColumnMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(navigatorSurface, [new Point(60, 20)]);
                await Task.Delay(180);
                var menu = (MenuFlyout)typeof(FileDetailsSurface).GetField("_columnMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(navigatorSurface)!;
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(navigatorSurface.XamlRoot).Last();
                await Capture(popup.Child, $"column-menu-{theme}.png");
                report["ColumnMenu" + theme] = menu.Items.OfType<ToggleMenuFlyoutItem>().Select(item => ((SolidColorBrush)item.Foreground).Color.ToString()).Distinct().ToArray();
                foreach (var item in menu.Items.OfType<ToggleMenuFlyoutItem>().Where(item => item.IsEnabled))
                {
                    var ink = ((SolidColorBrush)item.Foreground).Color;
                    Require(theme == AppThemeKind.Light ? ink.R < 140 : ink.R > 160, $"Column menu unreadable in {theme}: {ink}");
                }
                menu.Hide();
                await Task.Delay(80);
            }
            report["ColumnMenuLightDarkTextAndCheckmarks"] = true;
            var history = PolishDescendants(navigator).OfType<OperationHistoryButton>().First(b => b.IsLoaded && b.ActualHeight > 0);
            await Invoke((Button)history.FindName("HistoryButton"));
            var flyout = (Flyout)history.FindName("HistoryFlyout");
            Require(((FrameworkElement)history.FindName("EmptyText")).Visibility == Visibility.Visible, "History empty state missing.");
            Require(!((Button)history.FindName("UndoButton")).IsEnabled, "Empty history offered undo.");
            await CapturePopupAsync((FrameworkElement)flyout.Content, "operation-history-empty.png");
            flyout.Hide();
            var renamed = Path.Combine(fixture, "Reviewed.txt");
            File.Move(file, renamed);
            App.FileUndo.Push(FileUndoRecord.Relocated([new(file, renamed)]));
            await Until(() => PolishDescendants(navigator).OfType<OperationNotice>().Any(n => n.Visibility == Visibility.Visible));
            var notice = PolishDescendants(navigator).OfType<OperationNotice>().Single();
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Task.Delay(200);
                var background = PolishDescendants(notice).OfType<Border>().First().Background switch
                { SolidColorBrush solid => solid.Color, AcrylicBrush acrylic => acrylic.FallbackColor, _ => throw new InvalidOperationException("Unknown notice material.") };
                Require(theme == AppThemeKind.Light ? background.R > 150 : background.R < 120, $"Toast does not follow {theme}: {background}");
                await Capture(Content, $"operation-notice-{theme}.png");
                await Invoke((Button)history.FindName("HistoryButton"));
                await CapturePopupAsync((FrameworkElement)flyout.Content, $"operation-history-{theme}.png");
                flyout.Hide();
            }
            await Invoke((Button)history.FindName("HistoryButton"));
            await Invoke((Button)history.FindName("UndoButton"));
            await Until(() => File.Exists(file) && App.FileUndo.CanRedo);
            Require(!File.Exists(renamed), "Undo history did not restore the original filename.");
            await Invoke((Button)history.FindName("RedoButton"));
            await Until(() => File.Exists(renamed) && App.FileUndo.CanUndo);
            Require(!File.Exists(file), "History redo did not restore the rename.");
            await Invoke((Button)history.FindName("LocateButton"));
            await Until(() => TabHost.Content is NavigatorPage next && !ReferenceEquals(next, navigator) && next.IsLoaded && !next.ViewModel.IsLoading);
            var located = (FileDetailsSurface)((NavigatorPage)TabHost.Content).FindName("FileSurface");
            await Until(() => located.SelectedPaths().Contains(renamed));
            report["ThemeAwareToastEmptyHistoryUndoRedoAndLocate"] = true;
            report["Passed"] = true;
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "opening-settings-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static Rect Bounds(FrameworkElement element, UIElement relative) => element.TransformToVisual(relative).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
        static Point Center(FrameworkElement element, UIElement relative) => element.TransformToVisual(relative).TransformPoint(new(element.ActualWidth / 2, element.ActualHeight / 2));
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Until(Func<bool> predicate)
        { for (var i = 0; i < 160; i++) { if (predicate()) return; await Task.Delay(40); } throw new TimeoutException("Opening/settings fixture did not reach its expected state."); }
        static async Task Invoke(Button button)
        { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(180); }
    }
}
#endif
