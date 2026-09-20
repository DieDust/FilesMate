#if FILESMATE_UI_TEST
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using System.Text.Json;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    // Real ScrollViewer/ViewChanged/timer integration, without taking over desktop input.
    internal async Task RunMarqueeSmokeAsync()
    {
        var samples = new List<object>();
        var output = Path.Combine(AppContext.BaseDirectory, "marquee-results.json");
        try
        {
            var store = new EntryStore();
            store.Append(Enumerable.Range(1, 2000).Select(i => new FileEntryCore(i,
                $"File-{i:D4}.txt", 0, DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks,
                FileAttributes.Normal, EntryKind.File)).ToArray());
            var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, StringComparer.Ordinal, 1);
            Bind(store, index, 1);
            foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.Grid })
            {
                SetLayout(layout);
                await Scroll(0);
                Begin(new Point(0, 0), new Point(180, Scroller.ViewportHeight / 2));
                var first = _selection.Ids.ToArray();
                Require(first.Length > 0, "Initial selection empty");
                await Scroll(Scroller.ViewportHeight * 3);
                Require(first.All(_selection.Contains), "Scrolling down dropped anchor items");
                Require(_selection.Count > first.Length, "Selection did not expand beyond viewport");
                Require(Marquee.Margin.Top == 0 && Marquee.Height <= Scroller.ViewportHeight,
                    "Marquee visual escaped viewport");
                var expanded = _selection.Count;
                await Scroll(Scroller.ViewportHeight);
                Require(first.All(_selection.Contains) && _selection.Count < expanded,
                    "Reversing drag did not contract selection");
                var beforeWheel = Scroller.VerticalOffset;
                ScrollMarqueeWheel(-120, false);
                await Task.Delay(200);
                Require(Scroller.VerticalOffset > beforeWheel && first.All(_selection.Contains),
                    "Wheel during drag did not scroll down while preserving selection");
                var afterWheel = Scroller.VerticalOffset;
                ScrollMarqueeWheel(120, false);
                await Task.Delay(200);
                Require(Scroller.VerticalOffset < afterWheel && first.All(_selection.Contains),
                    "Wheel during drag did not scroll up while preserving selection");
                CancelMarquee();

                await Scroll(Scroller.ViewportHeight * 4);
                Begin(new Point(0, Scroller.ViewportHeight - 5), new Point(180, Scroller.ViewportHeight / 2));
                var lower = _selection.Ids.ToArray();
                Require(lower.Length > 0, "Upward initial selection empty");
                await Scroll(Scroller.ViewportHeight);
                Require(lower.All(_selection.Contains) && _selection.Count > lower.Length,
                    "Scrolling up dropped the lower anchor");
                CancelMarquee();

                await Scroll(0);
                Begin(new Point(0, 0), new Point(180, Scroller.ViewportHeight - 1));
                StartMarqueeAutoScroll();
                await Task.Delay(600);
                var autoOffset = Scroller.VerticalOffset;
                Require(autoOffset > ItemHeight() * 2, "Stationary edge pointer did not keep scrolling");
                Require(_selection.Contains(1), "Auto-scroll dropped first entry");
                OnPointerCaptureLost(this, null!);
                Require(!_marqueeScrollTimer!.IsRunning, "Capture loss retained timer");
                await Task.Delay(180);
                var stopped = Scroller.VerticalOffset;
                await Task.Delay(220);
                Require(Scroller.VerticalOffset == stopped, "Scroll continued after capture loss");
                Require(_selection.Contains(1), "Capture loss cleared selection");

                Begin(new Point(0, 0), new Point(180, 1));
                StartMarqueeAutoScroll();
                await Task.Delay(260);
                Require(Scroller.VerticalOffset < autoOffset, "Top edge did not auto-scroll upwards");
                CancelMarquee();
                Require(!_marqueeScrollTimer.IsRunning, "Cancel retained timer");
                samples.Add(new { Layout = layout.ToString(), Initial = first.Length, Expanded = expanded,
                    AutoScrollOffset = autoOffset, AnchorRetained = true, ReverseContracts = true,
                    WheelBothDirections = true, CaptureLossStops = true });
            }

            Begin(new Point(0, 0), new Point(180, Scroller.ViewportHeight - 1));
            StartMarqueeAutoScroll();
            Bind(null, null, 2);
            Require(!_marqueeScrollTimer!.IsRunning && !_dragging, "Navigation did not cancel drag");
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, samples,
                NavigationStops = true, RealScrollViewer = true, DesktopPointerInputUsed = false },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), samples }));
        }
        finally { CancelMarquee(); }

        void Begin(Point anchor, Point pointer)
        {
            _pointerDown = _dragging = true;
            _marqueeStart = ToContentPoint(anchor);
            _marqueePointer = pointer;
            Marquee.Visibility = Visibility.Visible;
            UpdateMarquee();
        }
        async Task Scroll(double offset)
        {
            Scroller.ChangeView(null, offset, null, true);
            await Task.Delay(240);
            Require(Math.Abs(Scroller.VerticalOffset - offset) < 2, "Scroll offset not applied");
        }
        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
#endif
