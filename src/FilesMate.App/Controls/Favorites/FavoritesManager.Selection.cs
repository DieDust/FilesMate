using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace FilesMate.App.Controls.Favorites;

public sealed partial class FavoritesManager
{
    private uint? _boxPointer;
    private Point _boxStart, _boxCurrent;
    private double _boxStartOffset;
    private bool _boxActive, _boxAdd, _boxToggle;
    private HashSet<EntryRow> _boxInitial = [];
    private ScrollViewer? _entriesScroller;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _boxTimer;

    private static bool IsSelectionModifier(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void InitializeBoxSelection()
    {
        EntriesArea.AddHandler(PointerPressedEvent, new PointerEventHandler(BoxPressed), true);
        EntriesArea.PointerMoved += BoxMoved;
        EntriesArea.PointerReleased += (_, e) => { if (_boxPointer == e.Pointer.PointerId) EndBoxSelection(); };
        EntriesArea.PointerCaptureLost += (_, _) => EndBoxSelection();
        Unloaded += (_, _) => EndBoxSelection();
    }

    private void BoxPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_busy || !e.GetCurrentPoint(EntriesArea).Properties.IsLeftButtonPressed) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != EntriesArea; node = VisualTreeHelper.GetParent(node))
            if (node is ListViewItem or ScrollBar or ButtonBase) return;
        _entriesScroller ??= FindScroller(Entries);
        Entries.Focus(FocusState.Pointer);
        if (_entriesScroller is null || !EntriesArea.CapturePointer(e.Pointer)) return;
        _boxPointer = e.Pointer.PointerId;
        _boxStart = _boxCurrent = e.GetCurrentPoint(EntriesArea).Position;
        _boxStartOffset = _entriesScroller.VerticalOffset;
        _boxAdd = IsSelectionModifier(VirtualKey.Shift);
        _boxToggle = IsSelectionModifier(VirtualKey.Control) && !_boxAdd;
        _boxInitial = Entries.SelectedItems.OfType<EntryRow>().ToHashSet();
        if (!_boxAdd && !_boxToggle) Entries.SelectedItems.Clear();
        e.Handled = true;
    }

    private void BoxMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_boxPointer != e.Pointer.PointerId) return;
        _boxCurrent = e.GetCurrentPoint(EntriesArea).Position;
        if (!_boxActive && Math.Abs(_boxCurrent.X - _boxStart.X) + Math.Abs(_boxCurrent.Y - _boxStart.Y) < 5) return;
        _boxActive = true;
        _boxTimer ??= DispatcherQueue.CreateTimer();
        if (!_boxTimer.IsRunning)
        {
            _boxTimer.Interval = TimeSpan.FromMilliseconds(32);
            _boxTimer.Tick -= BoxAutoScroll;
            _boxTimer.Tick += BoxAutoScroll;
            _boxTimer.Start();
        }
        ApplyBoxSelection();
        e.Handled = true;
    }

    private void BoxAutoScroll(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object args)
    {
        if (!_boxActive || _entriesScroller is null) return;
        var y = _boxCurrent.Y;
        var delta = y < 28 ? -18 : y > EntriesArea.ActualHeight - 28 ? 18 : 0;
        if (delta != 0)
            _entriesScroller.ChangeView(null, Math.Clamp(_entriesScroller.VerticalOffset + delta, 0, _entriesScroller.ScrollableHeight), null, true);
        ApplyBoxSelection();
    }

    private void ApplyBoxSelection()
    {
        if (_entriesScroller is null || Entries.ItemsPanelRoot is not ItemsStackPanel panel) return;
        var x1 = Math.Clamp(Math.Min(_boxStart.X, _boxCurrent.X), 0, EntriesArea.ActualWidth);
        var x2 = Math.Clamp(Math.Max(_boxStart.X, _boxCurrent.X), 0, EntriesArea.ActualWidth);
        var startY = _boxStart.Y + _boxStartOffset;
        var endY = Math.Clamp(_boxCurrent.Y, 0, EntriesArea.ActualHeight) + _entriesScroller.VerticalOffset;
        var y1 = Math.Min(startY, endY);
        var y2 = Math.Max(startY, endY);
        var top = Math.Clamp(y1 - _entriesScroller.VerticalOffset, 0, EntriesArea.ActualHeight);
        Canvas.SetLeft(SelectionRectangle, x1);
        Canvas.SetTop(SelectionRectangle, top);
        SelectionRectangle.Width = x2 - x1;
        SelectionRectangle.Height = Math.Max(0, Math.Min(EntriesArea.ActualHeight, y2 - _entriesScroller.VerticalOffset) - top);
        SelectionRectangle.Visibility = Visibility.Visible;

        var selected = _boxAdd || _boxToggle ? new HashSet<EntryRow>(_boxInitial) : [];
        var first = Math.Max(0, panel.FirstVisibleIndex);
        if (Entries.ContainerFromIndex(first) is ListViewItem item && x1 < Entries.ActualWidth && x2 > 0)
        {
            var bounds = item.TransformToVisual(EntriesArea).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            var stride = item.ActualHeight;
            if (Entries.ContainerFromIndex(first + 1) is ListViewItem next)
                stride = next.TransformToVisual(EntriesArea).TransformPoint(new Point()).Y - bounds.Y;
            if (stride > 0)
            {
                var origin = bounds.Y + _entriesScroller.VerticalOffset - first * stride;
                var from = Math.Max(0, (int)Math.Floor((y1 - origin) / stride));
                var to = Math.Min(_rows.Count - 1, (int)Math.Floor((y2 - origin) / stride));
                for (var i = from; i <= to; i++)
                {
                    if (_boxToggle && !selected.Add(_rows[i])) selected.Remove(_rows[i]);
                    else selected.Add(_rows[i]);
                }
            }
        }
        // Apply only the difference, preserving virtualization and avoiding repeated selection notifications.
        var current = Entries.SelectedItems.OfType<EntryRow>().ToHashSet();
        _refreshing = true;
        try
        {
            foreach (var row in current.Except(selected)) Entries.SelectedItems.Remove(row);
            foreach (var row in selected.Except(current)) Entries.SelectedItems.Add(row);
        }
        finally { _refreshing = false; }
        UpdateSelection();
    }

    private void EndBoxSelection()
    {
        _boxPointer = null;
        _boxActive = false;
        _boxTimer?.Stop();
        _boxInitial.Clear();
        SelectionRectangle.Visibility = Visibility.Collapsed;
        EntriesArea.ReleasePointerCaptures();
    }

    private static ScrollViewer? FindScroller(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scroller) return scroller;
            if (FindScroller(child) is { } nested) return nested;
        }
        return null;
    }
}
