#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Views;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunPinCheckboxSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        var samples = new List<object>();
        var failures = new List<string>();
        FileDetailsSurface? surface = null;
        var fixture = Path.Combine(AppContext.BaseDirectory, "pin-checkbox-fixture-" + Guid.NewGuid().ToString("N"));
        var originalPins = App.PinnedLocations.Load();
        try
        {
            if (!App.PinnedLocations.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(2200, 1500));
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            await App.AppearanceViewModel.SetAccentAsync(AccentKind.Default);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowGridFileSizes = false, ShowFullThumbnails = false });
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true });
            Directory.CreateDirectory(fixture);
            var original = Directory.CreateDirectory(Path.Combine(fixture, "原文件夹")).FullName;
            App.PinnedLocations.Add(original); App.NotifyPinnedLocationsChanged();
            await Wait(() => NavigationSidebar.PlaceFor(original, exact: true) is not null);
            surface = new FileDetailsSurface { Width = 640, Height = 220 };
            var host = new Border { Child = surface, Width = 640, Height = 220 };
            Theming.ThemeResources.Bind(host, Border.BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
            TabHost.Content = host;
            await Wait(() => surface.IsLoaded);
            var actions = new PaneFileActions(new WindowsLocalFileOperations(), () => [], () => fixture, () => original,
                error => throw new IOException(error), () => { }, App.VacateFoldersAsync, surface);
            var renamed = await actions.RenamePathAsync(original, "应用内改名");
            Require(renamed is not null && Directory.Exists(renamed) && App.PinnedLocations.IsPinned(renamed), "App rename did not update its pin");
            await Wait(() => NavigationSidebar.PlaceFor(renamed, exact: true)?.Label == "应用内改名");
            var external = Path.Combine(fixture, "外部改名");
            Directory.Move(renamed!, external);
            await Wait(() => App.PinnedLocations.IsPinned(external) && NavigationSidebar.PlaceFor(external, exact: true)?.Label == "外部改名");
            Require(new PinnedLocationStore(App.PinnedLocations.FilePath).IsPinned(external), "Updated target was not saved");
            report["Pins"] = new { InAppRename = true, ExternalRename = true, SidebarLabelAndTargetUpdated = true, Persisted = true };
            var store = new EntryStore();
            string[] names = ["项目资料", "测试文件.txt", "归档资料"];
            store.Append(names.Select((name, id) => new FileEntryCore(id, name, id == 1 ? 4096UL : 0, 0, 0,
                id == 1 ? FileAttributes.Normal : FileAttributes.Directory, id == 1 ? EntryKind.File : EntryKind.Directory)).ToArray());
            surface.Bind(store, EntryViewIndex.InSourceOrder(store, 1), 1);
            var opened = 0;
            surface.OpenRequested += (_, _) => opened++;
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List, FileLayoutKind.Grid })
                {
                    surface.SetLayout(layout); surface.SetGridSize(GridSizePreset.Large);
                    await Task.Delay(130);
                    foreach (var fileMode in Enum.GetValues<ItemOpeningMode>())
                    foreach (var folderMode in Enum.GetValues<ItemOpeningMode>())
                    {
                        await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { FileOpeningMode = fileMode, FolderOpeningMode = folderMode });
                        surface.RestoreSelectedNames([]);
                        await Task.Delay(45);
                        var items = Items();
                        Require(items.Length == 3, "Checkbox fixture items missing");
                        foreach (var item in items) Pointer(item, false);
                        var before = items.Select(item => Bounds((FrameworkElement)item.FindName("NameText"), surface)).ToArray();
                        foreach (var item in items) Require(Box(item).Visibility == Visibility.Collapsed, "Idle checkbox visible");
                        Pointer(items[0], true);
                        if (layout != FileLayoutKind.Grid)
                        {
                            Require(Box(items[0]).Visibility == Visibility.Collapsed, "Row hover exposed its icon checkbox");
                            Call(items[0], "OnIconPointerEntered", items[0], null);
                        }
                        await Task.Delay(25);
                        Require(Box(items[0]).Visibility == Visibility.Visible, "Hovered checkbox missing");
                        var boxBounds = Bounds(Box(items[0]), surface);
                        var parentBounds = Bounds((FrameworkElement)items[0].FindName(layout == FileLayoutKind.Grid ? "Root" : "IconFrame"), surface);
                        Require(parentBounds.Contains(new Point(boxBounds.X + boxBounds.Width / 2, boxBounds.Y + boxBounds.Height / 2)), "Checkbox is outside its icon/cell");
                        Require((bool)Call(surface, "IsSelectionToggleSource", Box(items[0]))!, "Checkbox was not excluded from ordinary item presses");
                        Click(items[0]); Require(surface.Selection.Count == 1 && surface.Selection.Contains(0) && Box(items[0]).IsChecked == true, "Checkbox did not select its folder");
                        Pointer(items[0], false);
                        Pointer(items[1], true);
                        if (layout != FileLayoutKind.Grid) Call(items[1], "OnIconPointerEntered", items[1], null);
                        Click(items[1]); Require(surface.Selection.Count == 2 && surface.Selection.Contains(0) && surface.Selection.Contains(1), "Checkbox replaced existing selection");
                        Require(Box(items[1]).IsChecked == true, "Selected checkbox is not checked");
                        Box(items[1]).UpdateLayout();
                        var mark = PolishDescendants(Box(items[1])).OfType<FontIcon>().Single(icon => icon.Name == "CheckGlyph");
                        Require(mark.Glyph == "\uE73E" && mark.Opacity == 1 && mark.FontSize == 14,
                            "Complete system check glyph was not immediately visible");
                        await Task.Delay(25);
                        for (var i = 0; i < items.Length; i++)
                        {
                            var after = Bounds((FrameworkElement)items[i].FindName("NameText"), surface);
                            Require(Math.Abs(before[i].X - after.X) < .1 && Math.Abs(before[i].Y - after.Y) < .1
                                && Math.Abs(before[i].Width - after.Width) < .1 && Math.Abs(before[i].Height - after.Height) < .1, "Checkbox moved filename layout");
                        }
                        if (fileMode == ItemOpeningMode.SingleClick && folderMode == ItemOpeningMode.SingleClick)
                        {
                            await Capture(host, $"pin-checkbox-{layout}-{theme}.png");
                        }
                        Click(items[1]); Require(surface.Selection.Count == 1 && surface.Selection.Contains(0) && Box(items[1]).IsChecked == false, "Unchecking removed wrong selection");
                        Pointer(items[1], false);
                        Require(Box(items[1]).Visibility == Visibility.Collapsed, "Checkbox remained after pointer exit");
                        foreach (var item in items)
                        {
                            var entry = item is FileRow row ? row.Entry : ((FileTile)item).Entry;
                            var index = item is FileRow r ? r.ViewIndex : ((FileTile)item).ViewIndex;
                            if (item is FileRow resetRow) resetRow.ResetVisual(); else ((FileTile)item).ResetVisual();
                            Require(Box(item).Visibility == Visibility.Collapsed && Box(item).IsChecked == false, "Recycled checkbox state retained");
                            if (item is FileRow boundRow) boundRow.Bind(index, entry, surface.Selection.Contains(entry.Id), null);
                            else ((FileTile)item).Bind(index, entry, surface.Selection.Contains(entry.Id), null);
                        }
                        samples.Add(new { Theme = theme.ToString(), Layout = layout.ToString(), FileMode = fileMode.ToString(), FolderMode = folderMode.ToString(), BoxWidth = boxBounds.Width, BoxHeight = boxBounds.Height });
                    }
                }
            }
            Require(opened == 0, "Checkbox clicks opened an item");
            report["Checkboxes"] = new { Cases = samples.Count, NoItemsOpened = true, LayoutUnchanged = true, Samples = samples };
            await RunFileConflictChecksAsync(host, fixture, report);
            Control[] Items() => PolishDescendants(surface).Where(item => item is FileRow { EntryId: >= 0 } || item is FileTile { EntryId: >= 0 })
                .Cast<Control>().OrderBy(item => item is FileRow row ? row.EntryId : ((FileTile)item).EntryId).ToArray();
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            surface?.ReleaseResources();
            App.PinnedLocations.Save(originalPins); App.NotifyPinnedLocationsChanged(); App.FileUndo.Clear();
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
        }
        report["Failures"] = failures; report["Passed"] = failures.Count == 0;
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "pin-checkbox-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static CheckBox Box(Control item) => (CheckBox)item.FindName("SelectionBox");
        static void Pointer(Control item, bool entered) => Call(item, entered ? "OnPointerEntered" : "OnPointerExited", item, null);
        static void Click(Control item) => Call(item, "OnSelectionBoxClick", Box(item), new RoutedEventArgs());
        static object? Call(object item, string method, params object?[] args) => item.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(item, args);
        static Rect Bounds(FrameworkElement element, UIElement relative) => element.TransformToVisual(relative).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
        static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> done)
        {
            for (var attempt = 0; attempt < 200; attempt++) { if (done()) return; await Task.Delay(50); }
            throw new TimeoutException("Native pin/checkbox fixture did not reach its expected state.");
        }
    }
}
#endif
