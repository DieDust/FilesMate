using System.Diagnostics;
using FilesMate.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private bool _scrollbarDragging;
    private uint _scrollbarPointer;
    private long _scrollbarOperation;
    private long _scrollbarLastFrame;
    private bool _scrollbarObservingFrames;
    private ScrollBar? _scrollbarBar;
    private long _scrollbarResourceAt;
    private bool _scrollReadyObserving, _scrollReadyVertical;
    private long _scrollReadyOperation, _scrollReadyReleasedAt;
    private double _scrollReadyTarget;

    private void InitializeScrollbarTracking()
    {
        AddHandler(PointerPressedEvent, new PointerEventHandler(ScrollbarPointerPressed), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(ScrollbarPointerMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(ScrollbarPointerEnded), true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler(ScrollbarPointerEnded), true);
        Unloaded += (_, _) => { StopScrollbarTracking(); StopScrollReadyObservation(); };
    }

    private void ScrollbarPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var thumb = false;
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is Thumb) thumb = true;
            if (source is not ScrollBar bar) continue;
            if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
            StopScrollbarTracking();
            StopScrollReadyObservation();
            _scrollbarBar = bar;
            if (thumb && ReferenceEquals(bar, ListColumnScroller)) _listColumnThumbDragging = true;
            _scrollbarDragging = true;
            _scrollbarPointer = e.Pointer.PointerId;
            _scrollbarOperation = PerformanceTrace.NextOperation();
            ExtendRecursiveInputQuietPeriod();
            PerformanceTrace.Record(PerformanceStage.ScrollbarDragStarted, generation: _generation,
                operation: _scrollbarOperation, count: _scrollbarPointer, auxiliary: bar.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Vertical ? 1 : 2);
            if (PerformanceTrace.Enabled)
            {
                _scrollbarLastFrame = Stopwatch.GetTimestamp();
                _scrollbarResourceAt = 0;
                _scrollbarObservingFrames = true;
                CompositionTarget.Rendering += ScrollbarRendering;
            }
            return;
        }
    }

    private void ScrollbarPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrollbarDragging || e.Pointer.PointerId != _scrollbarPointer) return;
        DeferListNameMeasurementForInput();
        ExtendRecursiveInputQuietPeriod();
        if (!PerformanceTrace.Enabled) return;
        var point = e.GetCurrentPoint(Scroller).Position;
        PerformanceTrace.Record(PerformanceStage.ScrollbarDragUpdated, generation: _generation, operation: _scrollbarOperation,
            count: (long)Math.Round(point.X * 1000), auxiliary: (long)Math.Round(point.Y * 1000));
    }

    private void ScrollbarPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrollbarDragging || e.Pointer.PointerId != _scrollbarPointer) return;
        DeferListNameMeasurementForInput();
        ExtendRecursiveInputQuietPeriod();
        StopScrollbarTracking();
    }

    private void StopScrollbarTracking()
    {
        var ended = _scrollbarDragging;
        if (_scrollbarObservingFrames) CompositionTarget.Rendering -= ScrollbarRendering;
        _scrollbarObservingFrames = false;
        if (_scrollbarDragging) PerformanceTrace.Record(PerformanceStage.ScrollbarDragCompleted,
            generation: _generation, operation: _scrollbarOperation, count: (long)Math.Round(ActiveScrollOffset * 1000));
        _scrollbarDragging = false;
        if (_listColumnThumbDragging)
        {
            _listColumnThumbDragging = false;
            if (_layout == FileLayoutKind.List && IsLoaded && !_resourcesReleased)
                ChangeScrollOffset(ListGeometry.LeftAt((int)Math.Round(ListColumnScroller.Value)));
        }
        if (ended && PerformanceTrace.Enabled && IsLoaded && !_resourcesReleased && _scrollbarBar is { } bar)
        {
            StopScrollReadyObservation();
            _scrollReadyVertical = bar.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Vertical;
            _scrollReadyTarget = ReferenceEquals(bar, ListColumnScroller) ? ListGeometry.LeftAt((int)Math.Round(bar.Value)) : bar.Value;
            _scrollReadyOperation = _scrollbarOperation;
            _scrollReadyReleasedAt = Stopwatch.GetTimestamp();
            _scrollReadyObserving = true;
            CompositionTarget.Rendering += ObserveScrollReady;
        }
        _scrollbarBar = null;
    }

    private void ScrollbarRendering(object? sender, object args)
    {
        if (_resourcesReleased || !IsLoaded) { StopScrollbarTracking(); return; }
        var now = Stopwatch.GetTimestamp();
        PerformanceTrace.Record(PerformanceStage.ScrollFrameCallbackProxy, generation: _generation, operation: _scrollbarOperation,
            durationTicks: now - _scrollbarLastFrame, count: (long)Math.Round(ActiveScrollOffset * 1000), auxiliary: _realized.Count + _tiles.Count);
        _scrollbarLastFrame = now;
        if (Stopwatch.GetElapsedTime(_scrollbarResourceAt, now).TotalMilliseconds >= 100)
        {
            _scrollbarResourceAt = now;
            Icons.ShellIconBinder.TraceScrollResources(_generation, _scrollbarOperation);
            PerformanceTrace.Record(PerformanceStage.ScrollManagedBytes, generation: _generation, operation: _scrollbarOperation,
                count: GC.GetTotalMemory(false), auxiliary: GC.GetGCMemoryInfo().TotalCommittedBytes);
            PerformanceTrace.Record(PerformanceStage.ScrollGcCounts, generation: _generation, operation: _scrollbarOperation,
                count: GC.CollectionCount(0), auxiliary: GC.CollectionCount(2));
        }
    }

    private void StopScrollReadyObservation()
    {
        if (_scrollReadyObserving) CompositionTarget.Rendering -= ObserveScrollReady;
        _scrollReadyObserving = false;
    }

    private void ObserveScrollReady(object? sender, object args)
    {
        if (_resourcesReleased || !IsLoaded || Stopwatch.GetElapsedTime(_scrollReadyReleasedAt).TotalSeconds > 3)
        { StopScrollReadyObservation(); return; }
        var offset = _scrollReadyVertical ? Scroller.VerticalOffset : Scroller.HorizontalOffset;
        if (Math.Abs(offset - _scrollReadyTarget) > 1) return;
        var viewport = _layout == FileLayoutKind.List ? Scroller.ViewportWidth : Scroller.ViewportHeight;
        var range = ThumbnailVisibleEntries(viewport, _layout == FileLayoutKind.List ? ListRows : Columns());
        if (range.IsEmpty) return;
        for (var i = range.First; i <= range.Last; ++i)
        {
            var element = Repeater.TryGetElement(i);
            if (element is FileRow row && row.ViewIndex == i && row.EntryId >= 0) continue;
            if (element is FileTile tile && tile.ViewIndex == i && tile.EntryId >= 0) continue;
            return;
        }
        PerformanceTrace.Record(PerformanceStage.ScrollFinalViewportReady, generation: _generation, operation: _scrollReadyOperation,
            durationTicks: Stopwatch.GetTimestamp() - _scrollReadyReleasedAt, count: range.Count, auxiliary: (long)Math.Round(offset * 1000));
        Icons.ShellIconBinder.TraceScrollResources(_generation, _scrollReadyOperation);
        StopScrollReadyObservation();
    }

    private void RecordScrollRowPrepared(int view, long ticks)
    {
        if (PerformanceTrace.Enabled) PerformanceTrace.Record(PerformanceStage.ScrollRowPrepared, generation: _generation,
            operation: _scrollbarDragging ? _scrollbarOperation : 0, count: view, durationTicks: ticks, auxiliary: _realized.Count + _tiles.Count);
    }
}
