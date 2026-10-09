using System.Diagnostics;
using FilesMate.App.Navigation;
using FilesMate.Core.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private EventHandler<object>? _recursiveLayoutObservation, _recursiveRenderObservation;
    private ItemsRepeater? _recursiveObservedRepeater;

    private void CancelRecursiveFrameObservation()
    {
        CancelOrdinaryFrameObservation();
        if (_recursiveLayoutObservation is not null && _recursiveObservedRepeater is not null)
            _recursiveObservedRepeater.LayoutUpdated -= _recursiveLayoutObservation;
        if (_recursiveRenderObservation is not null) CompositionTarget.Rendering -= _recursiveRenderObservation;
        _recursiveLayoutObservation = _recursiveRenderObservation = null;
        _recursiveObservedRepeater = null;
    }

    private void ObserveRecursiveFrame(EntryViewBinding binding, long bindingVersion)
    {
        if (!PerformanceTrace.Enabled || binding.DiagnosticOperation == 0) return;
        CancelRecursiveFrameObservation();
        var repeater = Repeater;
        _recursiveObservedRepeater = repeater;
        var started = Stopwatch.GetTimestamp();
        bool Owns() => !_resourcesReleased && bindingVersion == _bindingVersion && ReferenceEquals(binding, _items.Binding);
        _recursiveLayoutObservation = (_, _) =>
        {
            if (_recursiveLayoutObservation is not null) repeater.LayoutUpdated -= _recursiveLayoutObservation;
            _recursiveLayoutObservation = null;
            if (Owns()) PerformanceTrace.Record(PerformanceStage.RecursiveLayoutObserved, generation: binding.Generation,
                operation: binding.DiagnosticOperation, count: _realized.Count, durationTicks: Stopwatch.GetTimestamp() - started);
        };
        _recursiveRenderObservation = (_, _) =>
        {
            if (_recursiveRenderObservation is not null) CompositionTarget.Rendering -= _recursiveRenderObservation;
            _recursiveRenderObservation = null;
            // This is a real rendering callback observation, not a claim of GPU presentation or a captured first frame.
            if (Owns()) PerformanceTrace.Record(PerformanceStage.RecursiveRenderCallbackProxy, generation: binding.Generation,
                operation: binding.DiagnosticOperation, count: binding.Index.Count, durationTicks: Stopwatch.GetTimestamp() - started);
        };
        repeater.LayoutUpdated += _recursiveLayoutObservation;
        CompositionTarget.Rendering += _recursiveRenderObservation;
    }
}
