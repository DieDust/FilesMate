#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunListScrollSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        FileDetailsSurface? surface = null;
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(2350, 1400));
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } page && !page.ViewModel.IsLoading, "initial navigator");
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            surface = new FileDetailsSurface();
            TabHost.Content = surface;
            await Wait(() => surface.IsLoaded, "surface load");
            surface.SetLayout(FileLayoutKind.List);
            var scroller = (ScrollViewer)surface.FindName("Scroller");
            var repeater = (ItemsRepeater)Field(surface, "Repeater")!;
            var mediumName = "项目资料-" + new string('测', 35) + ".txt";
            Bind(2_000, mediumName, 1);
            await Measured();
            var width = Width();
            Require(width > 240 && width < 800, "Offscreen long name did not expand the column: " + width);
            Require(Geometry().WidthAt(0) < width, "Short-name columns inherited the long column width.");
            Require(surface.TrySelectByName(mediumName), "Long name cannot be selected.");
            await Wait(() => Rows().Any(r => r.Entry.Name == mediumName), "long name realization");
            await Task.Delay(150);
            var mediumText = (TextBlock)Rows().Single(r => r.Entry.Name == mediumName).FindName("NameText");
            report["NameMeasurement"] = new { ColumnWidth = width, ShortColumnWidth = Geometry().WidthAt(0),
                MeasurementFont = ((TextBlock)Field(surface, "_listNameMeasure")!).FontFamily.Source,
                NameCellWidth = ((FrameworkElement)Rows().Single(r => r.Entry.Name == mediumName).FindName("NameCell")).ActualWidth,
                RootWidth = ((FrameworkElement)Rows().Single(r => r.Entry.Name == mediumName).FindName("Root")).ActualWidth,
                mediumText.ActualWidth, mediumText.DesiredSize, mediumText.FontSize, FontFamily = mediumText.FontFamily.Source,
                FontWeight = mediumText.FontWeight.Weight, mediumText.IsTextTrimmed, mediumText.Text };
            Require(!mediumText.IsTextTrimmed && mediumText.Text == mediumName, "Name below the limit was truncated.");
            await Capture(Content, "list-long-name-Dark.png");
            report["OffscreenNamesMeasuredAndFullyVisible"] = new { Width = width, Name = mediumName };

            var cappedName = "项目资料-" + new string('测', 120) + ".txt";
            Bind(2_000, cappedName, 2);
            await Measured();
            Require(Width() == 800, "Maximum column width differs from the reference.");
            Require(Geometry().WidthAt(0) < 240, "The capped name widened unrelated columns.");
            Require(surface.TrySelectByName(cappedName), "Capped name cannot be selected.");
            await Wait(() => Rows().Any(r => r.Entry.Name == cappedName), "capped name realization");
            await Task.Delay(150);
            Require(((TextBlock)Rows().Single(r => r.Entry.Name == cappedName).FindName("NameText")).IsTextTrimmed,
                "Names beyond the upper limit should use ellipsis.");
            await Capture(Content, "list-width-limit-Dark.png");
            report["MaximumColumnWidth"] = new { Logical = Width(), RasterizationScale = surface.XamlRoot.RasterizationScale };

            // Exercise actual HWND wheel input as well as the target accumulator.
            // Directly calling ScrollListWheel cannot catch WinUI consuming a routed wheel event first.
            scroller.ChangeView(0, 0, null, true); await Task.Delay(100);
            surface.Focus(FocusState.Programmatic);
            var nativeTarget = BrowsingGetFocus();
            BrowsingGetWindowThreadProcessId(nativeTarget, out var nativeProcess);
            Require(nativeTarget != 0 && nativeProcess == Environment.ProcessId, "Native wheel target is outside this test process");
            var cursorPoint = scroller.TransformToVisual(Content).TransformPoint(new Point(60, 50));
            var cursorScale = surface.XamlRoot.RasterizationScale;
            var screenPoint = new ListWheelPoint { X = (int)Math.Round(cursorPoint.X * cursorScale), Y = (int)Math.Round(cursorPoint.Y * cursorScale) };
            ListWheelClientToScreen(NativeHandle, ref screenPoint);
            var packedScreen = (nint)((screenPoint.X & 0xffff) | (screenPoint.Y << 16));
            var nativeWindows = new List<nint> { NativeHandle };
            ListWheelEnumChildWindows(NativeHandle, (window, _) => { nativeWindows.Add(window); return true; }, 0);
            var nativeAttempts = new List<object>();
            var nativeOffsets = new List<double>();
            scroller.ViewChanged += ObserveNativeWheel;
            try
            {
                foreach (var window in nativeWindows)
                {
                    BrowsingGetWindowThreadProcessId(window, out var owner);
                    Require(owner == Environment.ProcessId, "Mouse fixture window belongs to another process");
                    var clientPoint = screenPoint;
                    ListWheelScreenToClient(window, ref clientPoint);
                    var packedClient = (nint)((clientPoint.X & 0xffff) | (clientPoint.Y << 16));
                    var className = new System.Text.StringBuilder(256);
                    ListWheelGetClassName(window, className, className.Capacity);
                    BrowsingPostMessage(window, 0x0200, 0, packedClient);
                    await Task.Delay(80);
                    BrowsingPostMessage(window, 0x020A, unchecked((nuint)((uint)(ushort)-120 << 16)), packedScreen);
                    await Task.Delay(350);
                    nativeAttempts.Add(new { Window = window.ToInt64(), Class = className.ToString(), Offset = scroller.HorizontalOffset });
                    if (scroller.HorizontalOffset > 1) break;
                }
                report["NativeWheelRoute"] = new { Offset = scroller.HorizontalOffset, Expected = Geometry().LeftAt(1), Offsets = nativeOffsets, Attempts = nativeAttempts };
                Require(Math.Abs(scroller.HorizontalOffset - Geometry().LeftAt(1)) < 1, "Native wheel did not move exactly one aligned column");
                Require(nativeOffsets.All(IsAligned), "Native wheel exposed a partial column");
                var nativeStopped = scroller.HorizontalOffset;
                await Task.Delay(400);
                Require(Math.Abs(scroller.HorizontalOffset - nativeStopped) < .5, "Native wheel continued or rebounded after the step");

                var inputSite = nativeWindows.First(window =>
                {
                    var name = new System.Text.StringBuilder(256);
                    ListWheelGetClassName(window, name, name.Capacity);
                    return name.ToString() == "InputSiteWindowClass";
                });
                for (var i = 0; i < 3; i++) await NativeWheel(-30);
                Require(Math.Abs(scroller.HorizontalOffset - nativeStopped) < .5, "A partial native notch exposed a partial column");
                await NativeWheel(-30);
                Require(Math.Abs(scroller.HorizontalOffset - Geometry().LeftAt(2)) < 1, "Child input site lost fractional wheel input");
                await NativeWheel(120);
                Require(Math.Abs(scroller.HorizontalOffset - Geometry().LeftAt(1)) < 1, "Native reverse step is not aligned");
                await NativeWheel(120, horizontal: true);
                Require(Math.Abs(scroller.HorizontalOffset - Geometry().LeftAt(2)) < 1, "Native horizontal step is not aligned");
                Require(nativeOffsets.All(IsAligned), "A child input site produced pixel scrolling");

                var guardedOffset = scroller.HorizontalOffset;
                var sidebarPoint = WindowNavigation.TransformToVisual(Content).TransformPoint(new Point(24, 90));
                var sidebarScreen = new ListWheelPoint { X = (int)Math.Round(sidebarPoint.X * cursorScale), Y = (int)Math.Round(sidebarPoint.Y * cursorScale) };
                ListWheelClientToScreen(NativeHandle, ref sidebarScreen);
                await NativeWheel(-120, position: (nint)((sidebarScreen.X & 0xffff) | (sidebarScreen.Y << 16)));
                Require(Math.Abs(scroller.HorizontalOffset - guardedOffset) < .5, "Sidebar wheel moved the file list");
                var popupMenu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
                popupMenu.Items.Add(new MenuFlyoutItem { Text = "Native wheel guard" });
                FilesMate.App.Theming.FlyoutTheme.FollowHost(popupMenu);
                popupMenu.ShowAt(scroller);
                await Task.Delay(150);
                try
                {
                    await NativeWheel(-120);
                    Require(Math.Abs(scroller.HorizontalOffset - guardedOffset) < .5, "Open popup wheel moved the list behind it");
                }
                finally { popupMenu.Hide(); await Task.Delay(100); }
                await NativeWheel(-120, modifiers: 8);
                Require(Math.Abs(scroller.HorizontalOffset - guardedOffset) < .5, "Ctrl+wheel was intercepted as a column step");
                report["NativeChildFractionalReverseHorizontalAndPopupGuards"] = true;

                async Task NativeWheel(int delta, bool horizontal = false, nuint modifiers = 0, nint? position = null)
                {
                    BrowsingPostMessage(inputSite, horizontal ? 0x020Eu : 0x020Au,
                        unchecked((nuint)((uint)(ushort)delta << 16)) | modifiers, position ?? packedScreen);
                    await Task.Delay(100);
                }
            }
            finally { scroller.ViewChanged -= ObserveNativeWheel; }
            void ObserveNativeWheel(object? sender, ScrollViewerViewChangedEventArgs e) => nativeOffsets.Add(scroller.HorizontalOffset);

            scroller.ChangeView(100.25, 0, null, true);
            await Wait(() => Math.Abs(scroller.HorizontalOffset - 100.25) < 1, "starting offset");
            Require(Field(surface, "_listWheelPresenter") is ScrollContentPresenter, "Wheel input was not intercepted below ScrollViewer.");
            var start = scroller.HorizontalOffset;
            var parameters = new object[] { 0x0068u, 0u, 0u, 0u };
            var parameterMethod = surface.GetType().GetMethod("SystemParametersInfoW", BindingFlags.Static | BindingFlags.NonPublic)!;
            var units = (bool)parameterMethod.Invoke(null, parameters)! ? (uint)parameters[2] : 3u;
            Require(units > 0, "This fixture requires mouse-wheel scrolling to be enabled.");
            var expected = Geometry().LeftAt(Geometry().ClampedColumnAt(start) + 3);
            var offsets = new List<double> { start };
            for (var i = 0; i < 12; i++)
            {
                Wheel(-30, false);
                await Task.Delay(10);
                offsets.Add(scroller.HorizontalOffset);
            }
            await Settled();
            offsets.Add(scroller.HorizontalOffset);
            Require(Math.Abs(scroller.HorizontalOffset - expected) < 1, $"Wheel input lost a column or scrolled twice: {scroller.HorizontalOffset} vs {expected}.");
            Require(offsets.Zip(offsets.Skip(1)).All(pair => pair.Second + .5 >= pair.First), "Column wheel input rebounded.");
            Require(offsets.Skip(1).All(offset => Math.Abs(offset - start) < .5 || IsAligned(offset)), "Wheel input left a partial column.");
            var stopped = scroller.HorizontalOffset;
            await Task.Delay(450);
            Require(Math.Abs(scroller.HorizontalOffset - stopped) < .5, "A second alignment happened after scrolling stopped.");
            report["WinUIColumnWheelAlignment"] = new { Start = start, Expected = expected, Stopped = stopped, Offsets = offsets };

            Wheel(120, false); await Settled();
            Require(scroller.HorizontalOffset < stopped, "Reverse wheel did not respond.");
            var beforeHorizontal = scroller.HorizontalOffset;
            Wheel(120, true); await Settled();
            Require(scroller.HorizontalOffset > beforeHorizontal, "Horizontal wheel direction is incorrect.");
            Wheel(-120, false); await Task.Delay(25);
            Call(surface, "StopListWheel");
            scroller.ChangeView(500.25, 0, null, true); await Task.Delay(100);
            var dragged = scroller.HorizontalOffset; await Task.Delay(350);
            Require(Math.Abs(scroller.HorizontalOffset - dragged) < .5 && !IsAligned(dragged), "Wheel handling changed the free scrollbar position.");
            Wheel(-30, false); await Task.Delay(150);
            Require(Math.Abs(scroller.HorizontalOffset - dragged) < .5, "A partial notch started pixel scrolling.");
            report["ReverseHorizontalAndScrollbarCancellation"] = true;

            Call(surface, "StopListWheel");
            scroller.ChangeView(0, 0, null, true); await Task.Delay(100);
            for (var i = 0; i < 8; i++) Wheel(-120, false);
            await Settled();
            Require(Math.Abs(scroller.HorizontalOffset - Geometry().LeftAt(8)) < 1, "Rapid notches lost pending column targets.");
            report["RapidNotchesKeepPendingColumns"] = true;
            Wheel(-120 * Geometry().ColumnCount, false); await Settled();
            Require(IsAligned(scroller.HorizontalOffset) && Math.Abs(scroller.HorizontalOffset - scroller.ScrollableWidth) < 1,
                "The right scroll limit clamped the target to a partial column.");
            Wheel(-120, false); await Settled();
            Require(!Motion().HasTarget && IsAligned(scroller.HorizontalOffset), "Input at the end retained a pending target.");
            Wheel(120, false); await Settled();
            Require(IsAligned(scroller.HorizontalOffset), "Backward scrolling from the last page left a partial column.");
            report["LastPageAndReverseStayAligned"] = true;

            var originalRows = Geometry().Rows;
            AppWindow.Resize(new(2350, 1000));
            await Wait(() => Geometry().Rows != originalRows, "row regrouping after resize"); await Measured();
            Require(Width() == 800 && Geometry().WidthAt(0) < 240, "Resize lost independent column widths.");
            AppWindow.Resize(new(2350, 1400));
            await Wait(() => Geometry().Rows == originalRows, "row regrouping after restoring size"); await Measured();
            report["ResizeRegroupsAndRemeasuresColumns"] = true;

            await App.AppearanceViewModel.SetFileTypographyAsync("Microsoft YaHei UI", 20, 16);
            surface.SetListZoom(160); await Measured();
            Require(Width() == 1_280, "Typography/zoom did not recompute the name width limit.");
            surface.SetListZoom(100);
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            Bind(2_000, cappedName, 3, Geometry().Rows + 1); await Measured();
            Require(Geometry().WidthAt(1) == 800 && Geometry().WidthAt(0) < 240, "Variable-width wheel fixture was not measured.");
            scroller.ChangeView(0, 0, null, true); await Task.Delay(100);
            Wheel(-120, false); await Settled();
            var narrowTarget = scroller.HorizontalOffset;
            Require(Math.Abs(narrowTarget - Geometry().LeftAt(1)) < 1, "The first column did not align.");
            Wheel(-120, false); await Settled();
            var wideTarget = scroller.HorizontalOffset;
            Require(Math.Abs(wideTarget - Geometry().LeftAt(2)) < 1 && Math.Abs(wideTarget - narrowTarget - 800) < 1,
                "The wheel used a fixed step instead of the wide column's width.");
            Wheel(120, false); await Settled();
            Require(Math.Abs(scroller.HorizontalOffset - narrowTarget) < 1, "Reverse wheel did not restore the wide column's start.");
            await Capture(Content, "list-column-alignment-Dark.png");
            report["VariableWidthColumnsAlignPerNotch"] = new { NarrowTarget = narrowTarget, WideTarget = wideTarget };

            Bind(100_000, "item-099999.txt", 4);
            var heartbeats = new List<double>();
            var slowHeartbeats = new List<object>();
            var heartbeatClock = Stopwatch.StartNew();
            var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (_, _) =>
            {
                var elapsed = heartbeatClock.Elapsed.TotalMilliseconds;
                heartbeats.Add(elapsed);
                if (elapsed >= 100) slowHeartbeats.Add(new { Milliseconds = elapsed,
                    MeasuredNames = Field(surface, "_listMeasureIndex"), MeasurementPending = Field(surface, "_listMeasurePending"),
                    scroller.HorizontalOffset, surface.RealizedCount, LastPreparationMilliseconds = surface.LastPrepareDuration.TotalMilliseconds });
                heartbeatClock.Restart();
            };
            var measureClock = Stopwatch.StartNew();
            timer.Start(); await Measured(); timer.Stop();
            var rowCount = (int)Field(Field(surface, "_listLayout")!, "Rows", property: true)!;
            var bound = ((int)Math.Ceiling(scroller.ViewportWidth / Width()) + 3) * rowCount;
            report["LargeFolder"] = new { Count = surface.Items.Count, Realized = surface.RealizedCount, Bound = bound,
                MeasurementMilliseconds = measureClock.Elapsed.TotalMilliseconds, MaximumHeartbeatDelayMilliseconds = heartbeats.DefaultIfEmpty().Max(),
                SlowHeartbeats = slowHeartbeats.ToArray() };
            Require(heartbeats.Count > 0 && heartbeats.Max() < 250, "100k name measurement blocked input.");
            Require(surface.RealizedCount <= bound, $"100k listing realized {surface.RealizedCount} rows (bound {bound}).");
            await Capture(Content, "list-100k-Dark.png");
            scroller.ChangeView(100.25, 0, null, true); await Task.Delay(100);
            var largeStart = scroller.HorizontalOffset;
            heartbeats.Clear(); slowHeartbeats.Clear(); heartbeatClock.Restart(); timer.Start();
            for (var i = 0; i < 24; i++) { Wheel(-30, false); await Task.Delay(8); }
            await Settled(); timer.Stop();
            var largeTarget = Geometry().LeftAt(Geometry().ClampedColumnAt(largeStart) + 6);
            report["LargeFolderColumnWheel"] = new { Expected = largeTarget, Actual = scroller.HorizontalOffset,
                MaximumHeartbeatDelayMilliseconds = heartbeats.DefaultIfEmpty().Max(), SlowHeartbeats = slowHeartbeats.ToArray() };
            Require(heartbeats.Count > 0 && heartbeats.Max() < 150, "Wheel scrolling in the 100k folder blocked input.");
            Require(Math.Abs(scroller.HorizontalOffset - largeTarget) < 1 && IsAligned(scroller.HorizontalOffset), "100k folder lost column wheel input.");
            Wheel(-120, false);
            surface.SetLayout(FileLayoutKind.Details); await Task.Delay(250);
            Require(!Motion().HasTarget, "Layout change retained a pending wheel target.");
            report["Passed"] = true;

            CompactListGeometry Geometry() => (CompactListGeometry)Field(Field(surface, "_listLayout")!, "Geometry", property: true)!;
            double Width() => Geometry().WidthAt(Geometry().ColumnCount - 1);
            bool IsAligned(double offset) => Math.Abs(offset - Geometry().LeftAt(Geometry().ClampedColumnAt(offset))) < 1;
            ListScrollState Motion() => (ListScrollState)Field(surface, "_listWheel")!;
            IEnumerable<FileRow> Rows() => PolishDescendants(surface).OfType<FileRow>().Where(r => r.EntryId >= 0 && r.IsLoaded);
            async Task Measured()
            {
                await Wait(() => !(bool)Field(surface, "_listMeasurePending")! && repeater.ActualWidth > 0, "name measurement");
                await Task.Delay(120);
            }
            async Task Settled() { await Wait(() => !Motion().HasTarget, "wheel alignment"); await Task.Delay(80); }
            void Bind(int count, string lastName, long generation, int? namedIndex = null)
            {
                var entries = Enumerable.Range(0, count).Select(i => new FileEntryCore(i,
                    i == (namedIndex ?? count - 1) ? lastName : $"item-{i:D6}.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File)).ToArray();
                var store = new EntryStore(); store.Append(entries);
                var index = namedIndex.HasValue ? EntryViewIndex.InSourceOrder(store, generation)
                    : EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, generation);
                surface.Bind(store, index, generation);
            }
            void Wheel(int delta, bool horizontal) => surface.GetType().GetMethod("ScrollListWheel", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(surface, [delta, horizontal]);
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            try { await Capture(Content, "list-scroll-failure.png"); } catch { }
        }
        finally { surface?.ReleaseResources(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "list-scroll-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static object? Field(object target, string name, bool property = false) => property
            ? target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)
            : target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
        static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> condition, string step)
        { for (var i = 0; i < 600; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(step); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ListWheelPoint { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    private static extern bool ListWheelClientToScreen(nint window, ref ListWheelPoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ScreenToClient")]
    private static extern bool ListWheelScreenToClient(nint window, ref ListWheelPoint point);
    private delegate bool ListWheelEnumWindow(nint window, nint data);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "EnumChildWindows")]
    private static extern bool ListWheelEnumChildWindows(nint window, ListWheelEnumWindow callback, nint data);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int ListWheelGetClassName(nint window, System.Text.StringBuilder value, int length);
}
#endif
