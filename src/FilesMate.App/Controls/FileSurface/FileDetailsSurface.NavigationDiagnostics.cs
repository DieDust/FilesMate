using System.Diagnostics;
using System.Runtime.CompilerServices;
using FilesMate.App.Navigation;
using FilesMate.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private long _ordinaryPreparedGeneration = -1, _ordinaryLayoutGeneration = -1;
    private long _ordinaryRenderedGeneration = -1, _ordinaryFilledGeneration = -1;
    private EventHandler<object>? _ordinaryLayoutObservation, _ordinaryRenderObservation;
    private ItemsRepeater? _ordinaryObservedRepeater;
    private sealed class PreparedNavigationRow { internal long Generation; }
    private ConditionalWeakTable<FrameworkElement, PreparedNavigationRow>? _navigationPreparedRows;

    private void RecordOrdinaryRowPrepared(FrameworkElement element, EntryViewBinding binding, int index)
    {
        if (!PerformanceTrace.Enabled) return;
        _navigationPreparedRows ??= new();
        _navigationPreparedRows.GetValue(element, static _ => new()).Generation = binding.Generation;
        if (_ordinaryPreparedGeneration == binding.Generation) return;
        _ordinaryPreparedGeneration = binding.Generation;
        PerformanceTrace.Record(PerformanceStage.OrdinaryFirstRowPrepared, generation: binding.Generation,
            operation: binding.DiagnosticOperation, count: index);
    }

    private void CancelOrdinaryFrameObservation()
    {
        if (_ordinaryLayoutObservation is not null && _ordinaryObservedRepeater is not null) _ordinaryObservedRepeater.LayoutUpdated -= _ordinaryLayoutObservation;
        if (_ordinaryRenderObservation is not null) CompositionTarget.Rendering -= _ordinaryRenderObservation;
        _ordinaryLayoutObservation = _ordinaryRenderObservation = null;
        _ordinaryObservedRepeater = null;
    }

    private void ObserveOrdinaryFrame(EntryViewBinding binding, long version)
    {
        if (!PerformanceTrace.Enabled || binding.Index.Count == 0 || _ordinaryFilledGeneration == binding.Generation) return;
        var started = Stopwatch.GetTimestamp();
        var repeater = Repeater;
        _ordinaryObservedRepeater = repeater;
        bool Owns() => !_resourcesReleased && version == _bindingVersion && ReferenceEquals(binding, _items.Binding) && ReferenceEquals(repeater, Repeater);
        int VisibleEntries()
        {
            var viewport = new Windows.Foundation.Rect(0, 0, Scroller.ActualWidth, Scroller.ActualHeight);
            var count = 0;
            foreach (var element in _realized.Cast<FrameworkElement>().Concat(_tiles))
            {
                if (_navigationPreparedRows is not { } preparedRows || !preparedRows.TryGetValue(element, out var prepared) || prepared.Generation != binding.Generation) continue;
                var index = element is FileRow row ? row.ViewIndex : ((FileTile)element).ViewIndex;
                var id = element is FileRow fileRow ? fileRow.EntryId : ((FileTile)element).EntryId;
                if (index < 0 || index >= binding.Index.Count || binding.Index.Snapshot[binding.Index[index]].Id != id
                    || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
                Windows.Foundation.Rect bounds;
                try { bounds = element.TransformToVisual(Scroller).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException) { continue; }
                if (bounds.Bottom > viewport.Top && bounds.Top < viewport.Bottom && bounds.Right > viewport.Left && bounds.Left < viewport.Right) count++;
            }
            return count;
        }
        _ordinaryLayoutObservation = (_, _) =>
        {
            if (!Owns()) { CancelOrdinaryFrameObservation(); return; }
            if (_ordinaryLayoutGeneration != binding.Generation && VisibleEntries() > 0)
            {
                _ordinaryLayoutGeneration = binding.Generation;
                PerformanceTrace.Record(PerformanceStage.OrdinaryFirstLayoutObserved, generation: binding.Generation,
                    operation: binding.DiagnosticOperation, count: binding.Index.Count);
            }
        };
        _ordinaryRenderObservation = (_, _) =>
        {
            if (!Owns()) { CancelOrdinaryFrameObservation(); return; }
            var visible = VisibleEntries();
            if (visible > 0 && _ordinaryRenderedGeneration != binding.Generation)
            {
                _ordinaryRenderedGeneration = binding.Generation;
                // Real rows intersect this viewport in a rendering callback;
                // this does not measure GPU presentation or the first captured frame.
                PerformanceTrace.Record(PerformanceStage.OrdinaryFirstContentRenderProxy, generation: binding.Generation,
                    operation: binding.DiagnosticOperation, count: visible, auxiliary: (long)_layout);
            }
            var slots = _layout == FileLayoutKind.List
                ? Math.Max(1, ListGeometry.VisibleRange(Scroller.HorizontalOffset, Scroller.ViewportWidth).LastVisible
                    - ListGeometry.VisibleRange(Scroller.HorizontalOffset, Scroller.ViewportWidth).FirstVisible + 1)
                : Math.Max(1, (int)Math.Ceiling(Scroller.ViewportHeight / Math.Max(1, ItemHeight()))) * (_layout == FileLayoutKind.Grid ? Columns() : 1);
            if (visible >= Math.Min(binding.Index.Count, slots))
            {
                _ordinaryFilledGeneration = binding.Generation;
                PerformanceTrace.Record(PerformanceStage.OrdinaryViewportFilledProxy, generation: binding.Generation,
                    operation: binding.DiagnosticOperation, count: visible, auxiliary: slots);
                CancelOrdinaryFrameObservation();
            }
            else if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2))
            {
                PerformanceTrace.Record(PerformanceStage.OrdinaryFrameObservationExpired, generation: binding.Generation,
                    operation: binding.DiagnosticOperation, count: visible, auxiliary: slots);
                CancelOrdinaryFrameObservation();
            }
        };
        repeater.LayoutUpdated += _ordinaryLayoutObservation;
        CompositionTarget.Rendering += _ordinaryRenderObservation;
    }
}
