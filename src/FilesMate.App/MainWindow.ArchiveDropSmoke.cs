#if FILESMATE_UI_TEST
using System.IO.Compression;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _archiveRuntimeTimer;
    internal static void TraceArchiveDrop(string kind, object state)
    {
        if (Environment.GetEnvironmentVariable("FILESMATE_ARCHIVE_DROP_TRACE") == "1")
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "archive-drop-trace.jsonl"),
                JsonSerializer.Serialize(new { kind, state }) + Environment.NewLine);
    }

    private async Task RunArchiveDropRuntimeAsync()
    {
        try
        {
            if (!App.PinnedLocations.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated test profile is required");
            AppWindow.IsShownInSwitchers = false;
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with
                { ShowFolderSizes = true, ShowGridFileSizes = true, DualPane = false, PaneCount = 1, OpenFoldersInNewTab = false });
            while (TabHost.Content is not NavigatorPage { IsLoaded: true } ready || ready.ViewModel.IsLoading)
                await Task.Delay(20);
            var page = (NavigatorPage)TabHost.Content;
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            var scroller = (ScrollViewer)surface.FindName("Scroller");
            var timer = DispatcherQueue.CreateTimer();
            _archiveRuntimeTimer = timer;
            var offsets = new List<double>();
            scroller.ViewChanged += (_, _) => offsets.Add(scroller.HorizontalOffset);
            timer.Interval = TimeSpan.FromMilliseconds(75);
            var lastCommand = string.Empty;
            double? pendingOffset = null;
            var geometryKey = string.Empty;
            var geometryMisses = new List<object>();
            var geometryPoints = 0;
            var realizedRows = (HashSet<FileRow>)typeof(FileDetailsSurface).GetField("_realized",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
            var realizedTiles = (HashSet<FileTile>)typeof(FileDetailsSurface).GetField("_tiles",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (pendingOffset is { } requested)
                    {
                        surface.UpdateLayout();
                        scroller.ChangeView(0, Math.Min(requested, scroller.ScrollableHeight), null, true);
                        pendingOffset = null;
                    }
                    var path = Path.Combine(AppContext.BaseDirectory, "archive-drop-command.json");
                    if (File.Exists(path))
                    {
                        var command = File.ReadAllText(path);
                        if (command != lastCommand)
                        {
                            using var data = JsonDocument.Parse(command);
                            var layout = Enum.Parse<FileLayoutKind>(data.RootElement.GetProperty("Layout").GetString()!);
                            surface.SetLayout(layout);
                            surface.SetGridSize(GridSizePreset.Large);
                            if (data.RootElement.TryGetProperty("Offset", out var offset))
                                pendingOffset = offset.GetDouble();
                            lastCommand = command;
                            if (data.RootElement.TryGetProperty("Selected", out var selected))
                                foreach (var tile in PolishDescendants(surface).OfType<FileTile>()) tile.SetSelected(selected.GetBoolean());
                        }
                    }
                    var measuring = (bool)typeof(FileDetailsSurface).GetField("_listMeasurePending",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
                    var nextGeometryKey = $"{lastCommand}/{scroller.ActualWidth}/{scroller.ActualHeight}/{scroller.VerticalOffset}/{scroller.HorizontalOffset}/{measuring}/{realizedRows.Count}/{realizedTiles.Count}/{geometryPoints == 0}";
                    var probeGeometry = nextGeometryKey != geometryKey || geometryPoints == 0;
                    if (probeGeometry) { geometryKey = nextGeometryKey; geometryMisses.Clear(); geometryPoints = 0; }
                    var entries = realizedTiles.Cast<FrameworkElement>().Concat(realizedRows).Where(element =>
                            element is FileRow { IsLoaded: true, EntryId: >= 0 } or FileTile { IsLoaded: true, EntryId: >= 0 })
                        .Select(element =>
                        {
                            var item = element is FileTile tile ? tile.Entry : ((FileRow)element).Entry;
                            var chrome = (FrameworkElement)element.FindName("Root");
                            var inner = chrome is Border border ? (FrameworkElement)border.Child : chrome;
                            var groups = VisualStateManager.GetVisualStateGroups((FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, 0));
                            var state = groups.FirstOrDefault()?.CurrentState?.Name;
                            var position = chrome.TransformToVisual(element).TransformPoint(new Point());
                            var rootPosition = chrome.TransformToVisual(null).TransformPoint(new Point());
                            if (probeGeometry && item.Kind == FilesMate.Core.Entries.EntryKind.Directory)
                            {
                                var insetPixel = 1 / surface.XamlRoot.RasterizationScale;
                                foreach (var x in new[] { insetPixel, chrome.ActualWidth / 2, chrome.ActualWidth - insetPixel })
                                foreach (var y in new[] { insetPixel, chrome.ActualHeight / 2, chrome.ActualHeight - insetPixel })
                                {
                                    var probe = chrome.TransformToVisual(null).TransformPoint(new Point(x, y));
                                    if (!surface.ContainsExternalDragPoint(probe)) continue;
                                    geometryPoints++;
                                    var destination = surface.ExternalDropDestinationAt(probe);
                                    if (Path.GetFileName(destination) != item.Name)
                                        geometryMisses.Add(new { item.Name, probe.X, probe.Y, destination });
                                }
                            }
                            return new { item.Name, State = state, ItemHeight = element.ActualHeight, ChromeHeight = chrome.ActualHeight,
                                ChromeBottom = position.Y + chrome.ActualHeight, InnerHeight = inner.ActualHeight,
                                InnerDesiredHeight = inner.DesiredSize.Height,
                                ChromeInset = chrome is Border inset ? inset.BorderThickness.Bottom + inset.Padding.Bottom : 0,
                                RootX = rootPosition.X, RootY = rootPosition.Y, ChromeWidth = chrome.ActualWidth };
                        }).ToArray();
                    var report = new { Ready = pendingOffset is null, ProcessId = Environment.ProcessId, Window = NativeHandle.ToInt64(),
                        Updated = DateTime.UtcNow, Command = lastCommand, Scale = surface.XamlRoot.RasterizationScale,
                        Measuring = measuring,
                        Offset = scroller.VerticalOffset, HorizontalOffset = scroller.HorizontalOffset,
                        ScrollableWidth = scroller.ScrollableWidth, HorizontalMode = scroller.HorizontalScrollMode.ToString(),
                        HintVisible = ((Border)surface.FindName("ArchiveDropHint")).Visibility.ToString(),
                        Hint = ((TextBlock)surface.FindName("ArchiveDropCaption")).Text,
                        NativeLocations = NativeDropLocations(),
                        UndoKind = App.FileUndo.Latest?.Kind.ToString(), CanUndo = App.FileUndo.CanUndo,
                        GeometryPoints = geometryPoints, GeometryMisses = geometryMisses,
                        Columns = layoutColumns(), Offsets = offsets.ToArray(), Entries = entries };
                    var reportPath = Path.Combine(AppContext.BaseDirectory, "archive-drop-runtime.json");
                    File.WriteAllText(reportPath + ".tmp", JsonSerializer.Serialize(report));
                    File.Move(reportPath + ".tmp", reportPath, true);
                }
                catch (Exception error) { TraceArchiveDrop("RuntimeError", error.ToString()); }
            };
            timer.Start();
            object[] NativeDropLocations()
            {
                object Location(string name, FrameworkElement element)
                {
                    var position = element.TransformToVisual(null).TransformPoint(default);
                    return new { Name = name, RootX = position.X, RootY = position.Y,
                        ChromeWidth = element.ActualWidth, ChromeHeight = element.ActualHeight };
                }
                var locations = new List<object>();
                if (Tabs.SelectedItem is Microsoft.UI.Xaml.Controls.TabViewItem { IsLoaded: true } tab)
                    locations.Add(Location("@tab", tab));
                var bar = page.NativeDropOmnibar;
                var crumbs = (List<Grid>)typeof(Controls.Omnibar.Omnibar).GetField("_crumbViews",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(bar)!;
                var crumb = crumbs.LastOrDefault(c => c.IsLoaded && c.ActualWidth > 0 && c.Visibility == Visibility.Visible
                    && ReferenceEquals(bar.NativeCrumbAt(c.TransformToVisual(null).TransformPoint(new Point(c.ActualWidth / 2, c.ActualHeight / 2))), c));
                if (crumb is not null) locations.Add(Location("@crumb", crumb));
                return locations.ToArray();
            }
            double[] layoutColumns()
            {
                var geometry = (CompactListGeometry)typeof(FileDetailsSurface).GetField("_listGeometry",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
                return Enumerable.Range(0, geometry.ColumnCount).Select(geometry.LeftAt).ToArray();
            }
        }
        catch (Exception error) { TraceArchiveDrop("RuntimeSetupError", error.ToString()); }
    }

    private async Task RunArchiveDropSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        var checks = new List<object>();
        try
        {
            Require(App.PinnedLocations.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase), "An isolated test profile is required");
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "archive-drop-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            var child = Directory.CreateDirectory(Path.Combine(fixture, "child")).FullName;
            await File.WriteAllTextAsync(Path.Combine(fixture, "existing.txt"), "existing target file");
            var archivePath = Path.Combine(fixture, "source.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (var stream = new StreamWriter(archive.CreateEntry("received.txt").Open()))
                stream.Write("archive drop payload");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(2400, 1600));
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with
                { ShowFolderSizes = false, DualPane = false, PaneCount = 1, OpenFoldersInNewTab = false });
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } initial && !initial.ViewModel.IsLoading);
            AddNavigatorTab(fixture);
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } ready
                && !ready.ViewModel.IsLoading && ready.ViewModel.ItemCount == 3);
            var page = (NavigatorPage)TabHost.Content;
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            var scroller = (ScrollViewer)surface.FindName("Scroller");
            foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List, FileLayoutKind.Grid })
            {
                surface.SetLayout(layout);
                await Task.Delay(200);
                surface.UpdateLayout();
                foreach (var name in new[] { "child", "existing.txt", "source.zip" })
                {
                    FrameworkElement item = layout == FileLayoutKind.Grid
                        ? PolishDescendants(surface).OfType<FileTile>().First(e => e.Entry.Name == name)
                        : PolishDescendants(surface).OfType<FileRow>().First(e => e.Entry.Name == name);
                    var point = item.TransformToVisual(null).TransformPoint(new Point(16, 12));
                    var expected = name == "child" ? child : fixture;
                    var actual = surface.ExternalDropDestinationAt(point);
                    checks.Add(new { Layout = layout.ToString(), Target = name, Expected = expected, Actual = actual });
                    Require(actual == expected, $"{layout}: dropping on {name} did not resolve to its destination folder");
                }
                var origin = scroller.TransformToVisual(null).TransformPoint(new Point());
                var background = new Point(origin.X + scroller.ActualWidth - 16, origin.Y + scroller.ActualHeight - 16);
                Require(surface.ExternalDropDestinationAt(background) == fixture, layout + ": background destination");
                Require(surface.ExternalDropDestinationAt(new Point(0, 0)) is null, layout + ": title/toolbar is not a destination");
                surface.IsFolderWritable = false;
                Require(surface.ExternalDropDestinationAt(background) is null, layout + ": unavailable destination");
                surface.IsFolderWritable = true;
                surface.IsPortableDevice = true;
                Require(surface.ExternalDropDestinationAt(background) is null, layout + ": portable devices do not accept this filesystem protocol");
                surface.IsPortableDevice = false;
                checks.Add(new { Layout = layout.ToString(), Background = true, OutsideRejected = true,
                    UnavailableRejected = true, PortableDeviceRejected = true });
            }
            report["Fixture"] = fixture;
            report["Passed"] = true;
        }
        catch (Exception error)
        {
            report["Passed"] = false;
            report["Error"] = error.ToString();
        }
        report["Checks"] = checks;
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "archive-drop-smoke.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        static async Task Wait(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(20);
            Require(ready(), "Archive drop fixture did not settle");
        }
    }
}
#endif
