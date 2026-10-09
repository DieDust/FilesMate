using FilesMate.App.Icons;
using FilesMate.App.Services;
using Microsoft.UI.Dispatching;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private readonly ThumbnailViewportPolicy _thumbnailViewport = new();
    private ThumbnailViewportDecision _thumbnailDecision;
    private long _thumbnailContentEpoch = 1;
    private long _thumbnailRetentionOwner = ShellIconBinder.CreateRetentionOwner();
    private DispatcherQueueTimer? _thumbnailSettleTimer;
    private long _thumbnailSettleEpoch;
    private (FileLayoutKind Layout, double Width, double Height, double Stride, int Bands, int Pixels, CompactListGeometry? List) _thumbnailGeometry;
    private bool _thumbnailHasGeometry;
    private CancellationTokenSource? _thumbnailPrefetchCancellation;
    private bool _thumbnailPrefetchRunning;
    private bool _thumbnailAwaitingObservation;
    private long _thumbnailPrefetchVersion;
    private long _thumbnailPrefetchedVersion = -1;

    private void ResetThumbnailViewport(bool invalidateEntries = false)
    {
        if (invalidateEntries) ++_thumbnailContentEpoch;
        _thumbnailSettleTimer?.Stop();
        _thumbnailViewport.Reset();
        _thumbnailHasGeometry = false;
        _thumbnailDecision = default;
        _thumbnailAwaitingObservation = true;
        CancelThumbnailPrefetch();
        foreach (var row in _realized.ToArray()) row.UpdateThumbnailDemand(ThumbnailLoadMode.Suspended, _thumbnailRetentionOwner);
        foreach (var tile in _tiles.ToArray()) tile.UpdateThumbnailDemand(ThumbnailLoadMode.Suspended, _thumbnailRetentionOwner);
        ShellIconBinder.ReleaseRetainedImages(_thumbnailRetentionOwner);
        _thumbnailRetentionOwner = ShellIconBinder.CreateRetentionOwner();
    }

    private ThumbnailLoadMode ThumbnailModeFor(int viewIndex) => !IsLoaded || _resourcesReleased || _visualsDetached
        ? ThumbnailLoadMode.Suspended : _thumbnailDecision.DemandFor(viewIndex) switch
        {
            ThumbnailDemand.Visible => ThumbnailLoadMode.Active,
            ThumbnailDemand.DeferredVisible => ThumbnailLoadMode.VisibleDeferred,
            _ => ThumbnailLoadMode.Suspended,
        };

    private void ObserveThumbnailViewport()
    {
        if (!IsLoaded || _resourcesReleased || _visualsDetached) return;
        var viewport = _layout == FileLayoutKind.List ? Scroller.ViewportWidth : Scroller.ViewportHeight;
        var bands = _layout == FileLayoutKind.List ? ListRows : Columns();
        var dipSize = _layout == FileLayoutKind.Grid ? (int)Math.Round(EffectiveGridPreset.IconSize) : (int)FileColumnLayout.DetailsIconSize;
        var pixels = ShellIconBinder.ThumbnailRequestPixels(dipSize, XamlRoot?.RasterizationScale ?? 1);
        var geometry = (_layout, Scroller.ViewportWidth, Scroller.ViewportHeight, ItemHeight(), bands, pixels,
            _layout == FileLayoutKind.List ? ListGeometry : null);
        if (_thumbnailHasGeometry && _thumbnailGeometry != geometry) ResetThumbnailViewport();
        _thumbnailGeometry = geometry;
        _thumbnailHasGeometry = true;
        var visible = ThumbnailVisibleEntries(viewport, bands);
        var decision = _thumbnailViewport.Observe(_inputClock.ElapsedMilliseconds, ActiveScrollOffset,
            viewport, visible, _items.Count, pixels);
        _thumbnailAwaitingObservation = false;
        ApplyThumbnailDecision(decision);
        StartThumbnailPrefetch();
        if (!decision.Fast)
        {
            _thumbnailSettleTimer?.Stop();
            return;
        }
        _thumbnailSettleTimer ??= CreateThumbnailSettleTimer();
        _thumbnailSettleEpoch = decision.Epoch;
        _thumbnailSettleTimer.Stop();
        // IsIntermediate=false also occurs for wheel steps. Only elapsed time
        // since the last motion may release the deferred visible requests.
        _thumbnailSettleTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1,
            decision.SettleAtMilliseconds - _inputClock.ElapsedMilliseconds));
        _thumbnailSettleTimer.Start();
    }

    private ThumbnailEntryRange ThumbnailVisibleEntries(double viewport, int entriesPerBand)
    {
        if (_items.Count == 0 || !double.IsFinite(viewport) || viewport <= 0) return ThumbnailEntryRange.Empty;
        int first, last;
        if (_layout == FileLayoutKind.List)
        {
            first = ListGeometry.ClampedColumnAt(Scroller.HorizontalOffset);
            last = ListGeometry.ClampedColumnAt(Math.BitDecrement(Scroller.HorizontalOffset + viewport));
        }
        else
        {
            var stride = ItemHeight();
            if (!double.IsFinite(stride) || stride <= 0) return ThumbnailEntryRange.Empty;
            first = (int)Math.Min(int.MaxValue, Math.Max(0, Math.Floor(Scroller.VerticalOffset / stride)));
            last = (int)Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling((Scroller.VerticalOffset + viewport) / stride) - 1));
        }
        return ThumbnailEntryRange.FromBands(first, last, entriesPerBand, _items.Count);
    }

    private bool RetainClearingThumbnail(int viewIndex)
    {
        if (!IsLoaded || _resourcesReleased || _visualsDetached || !_thumbnailHasGeometry
            || _thumbnailGeometry.Layout != _layout || _thumbnailGeometry.Width != Scroller.ViewportWidth
            || _thumbnailGeometry.Height != Scroller.ViewportHeight) return false;
        var viewport = _layout == FileLayoutKind.List ? Scroller.ViewportWidth : Scroller.ViewportHeight;
        var bands = _layout == FileLayoutKind.List ? ListRows : Columns();
        if (_thumbnailGeometry.Bands != bands || _thumbnailGeometry.Stride != ItemHeight()
            || _layout == FileLayoutKind.List && !ReferenceEquals(_thumbnailGeometry.List, ListGeometry)) return false;
        // Clearing may precede ViewChanged. Purely sample the new demand here;
        // never resume other presenters while ItemsSource is retiring them.
        var decision = _thumbnailViewport.Observe(_inputClock.ElapsedMilliseconds, ActiveScrollOffset,
            viewport, ThumbnailVisibleEntries(viewport, bands), _items.Count, _thumbnailGeometry.Pixels);
        if (decision.Visible != _thumbnailDecision.Visible || decision.Prefetch != _thumbnailDecision.Prefetch)
        {
            _thumbnailAwaitingObservation = true;
            CancelThumbnailPrefetch();
        }
        return decision.Retain.Contains(viewIndex);
    }

    private DispatcherQueueTimer CreateThumbnailSettleTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (!IsLoaded || _resourcesReleased || _visualsDetached) return;
            var decision = _thumbnailViewport.Settle(_inputClock.ElapsedMilliseconds, _thumbnailSettleEpoch);
            ApplyThumbnailDecision(decision);
            if (decision.Fast) ObserveThumbnailViewport();
        };
        return timer;
    }

    private void ApplyThumbnailDecision(ThumbnailViewportDecision decision)
    {
        if (_thumbnailDecision == decision) return;
        // A speed-only sample need not cancel the one raw prefetch owner.
        var replacePrefetch = _thumbnailDecision.Visible != decision.Visible
            || _thumbnailDecision.Prefetch != decision.Prefetch || _thumbnailDecision.Epoch != decision.Epoch;
        var sameDemands = !replacePrefetch && _thumbnailDecision.Retain == decision.Retain
            && _thumbnailDecision.Fast == decision.Fast;
        _thumbnailDecision = decision;
        if (replacePrefetch) CancelThumbnailPrefetch();
        // Speed and settle-deadline changes alone do not change any presenter's demand.
        // Keep the newest timer sample without walking every realized element again.
        if (sameDemands)
        {
            if (_scrollbarDragging) FilesMate.Core.Diagnostics.PerformanceTrace.Record(FilesMate.Core.Diagnostics.PerformanceStage.ScrollDemandShapeSkipped,
                generation: _generation, operation: _scrollbarOperation, count: _realized.Count + _tiles.Count);
            return;
        }
        if (_scrollbarDragging) FilesMate.Core.Diagnostics.PerformanceTrace.Record(FilesMate.Core.Diagnostics.PerformanceStage.ScrollDemandShapeApplied,
            generation: _generation, operation: _scrollbarOperation, count: _realized.Count + _tiles.Count);
        // A slow moved delta can leave fast mode before the common quiet
        // deadline. Only an elapsed deadline authorizes skipping cold debounce.
        var settled = !decision.Fast && _inputClock.ElapsedMilliseconds >= decision.SettleAtMilliseconds;
        foreach (var row in _realized.ToArray())
            if (row.ThumbnailContentEpoch == _thumbnailContentEpoch && Repeater.GetElementIndex(row) == row.ViewIndex)
                row.UpdateThumbnailDemand(ThumbnailModeFor(row.ViewIndex), _thumbnailRetentionOwner,
                    decision.Retain.Contains(row.ViewIndex), settled);
        foreach (var tile in _tiles.ToArray())
            if (tile.ThumbnailContentEpoch == _thumbnailContentEpoch && Repeater.GetElementIndex(tile) == tile.ViewIndex)
                tile.UpdateThumbnailDemand(ThumbnailModeFor(tile.ViewIndex), _thumbnailRetentionOwner,
                    decision.Retain.Contains(tile.ViewIndex), settled);
        StartThumbnailPrefetch();
    }

    private void CancelThumbnailPrefetch()
    {
        ++_thumbnailPrefetchVersion;
        _thumbnailPrefetchCancellation?.Cancel();
    }

    private void StartThumbnailPrefetch()
    {
        if (IsMemoryPresentation) return;
        if (_thumbnailPrefetchRunning || _thumbnailAwaitingObservation || _thumbnailPrefetchedVersion == _thumbnailPrefetchVersion
            || !IsLoaded || _resourcesReleased || _visualsDetached || _thumbnailDecision.Fast
            || _thumbnailDecision.Prefetch.IsEmpty) return;
        _thumbnailPrefetchRunning = true;
        var cancellation = new CancellationTokenSource();
        _thumbnailPrefetchCancellation = cancellation;
        _ = PrefetchThumbnailsAsync(_thumbnailPrefetchVersion, _thumbnailDecision, cancellation);
    }

    private bool VisibleThumbnailsPending(ThumbnailEntryRange visible)
    {
        for (var index = visible.First; index <= visible.Last; index++)
        {
            var element = Repeater.TryGetElement(index);
            if (element is FileRow row && row.ThumbnailContentEpoch == _thumbnailContentEpoch && row.ViewIndex == index)
            {
                if (row.HasPendingThumbnail) return true;
            }
            else if (element is FileTile tile && tile.ThumbnailContentEpoch == _thumbnailContentEpoch && tile.ViewIndex == index)
            {
                if (tile.HasPendingThumbnail) return true;
            }
            else return true; // Do not race ahead of the next layout's visible presenters.
        }
        return false;
    }

    private async Task PrefetchThumbnailsAsync(long version, ThumbnailViewportDecision decision,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            var range = decision.Prefetch;
            var step = decision.Direction;
            var index = step > 0 ? range.First : range.Last;
            var pixels = _thumbnailGeometry.Pixels;
            var visibleReady = false;
            while (range.Contains(index))
            {
                token.ThrowIfCancellationRequested();
                if (version != _thumbnailPrefetchVersion) return;
                if (!_items.TryGetEntry(index, out var entry)) break;
                var kind = FileTypeIconCatalog.Classify(entry);
                if (kind is FileIconKind.Image or FileIconKind.Video)
                {
                    // Text and folders have no thumbnail to prefetch. Check
                    // visible native presenters only when admitting media, and
                    // once after they finish for this unchanged viewport epoch.
                    if (!visibleReady)
                    {
                        if (VisibleThumbnailsPending(decision.Visible))
                        {
                            await Task.Delay(40, token);
                            continue;
                        }
                        visibleReady = true;
                    }
                    var path = ResolvePath?.Invoke(entry);
                    token.ThrowIfCancellationRequested();
                    if (version != _thumbnailPrefetchVersion) return;
                    if (!string.IsNullOrWhiteSpace(path)
                        && !path.StartsWith(FilesMate.Platform.Windows.Shell.PortableDeviceLocation.Prefix, StringComparison.Ordinal)
                        && FileTypeIconCatalog.ClassifyPath(path, false) is (FileIconKind.Image or FileIconKind.Video)
                        && !await ShellIconBinder.TryPrefetchThumbnailAsync(path, pixels, token))
                    {
                        await Task.Delay(60, token);
                        continue;
                    }
                }
                index += step;
            }
            if (version == _thumbnailPrefetchVersion) _thumbnailPrefetchedVersion = version;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Viewport thumbnail prefetch failed: {0}", error);
            if (version == _thumbnailPrefetchVersion) _thumbnailPrefetchedVersion = version;
        }
        finally
        {
            if (ReferenceEquals(_thumbnailPrefetchCancellation, cancellation)) _thumbnailPrefetchCancellation = null;
            cancellation.Dispose();
            _thumbnailPrefetchRunning = false;
            if (version != _thumbnailPrefetchVersion) StartThumbnailPrefetch();
        }
    }
}
