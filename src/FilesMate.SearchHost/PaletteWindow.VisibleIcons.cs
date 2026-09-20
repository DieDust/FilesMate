using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private readonly DispatcherTimer _visibleIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Dictionary<SearchRow, CancellationTokenSource> _visibleIconLoads = new();
    private HashSet<SearchRow> _visibleIconRows = new();

    private void QueueVisibleIcons()
    {
        if (!IsVisible || _iconQuery is null) return;
        // Throttle continuous scrolling without postponing work indefinitely.
        if (!_visibleIconsTimer.IsEnabled) _visibleIconsTimer.Start();
    }

    private void RefreshVisibleIcons()
    {
        _visibleIconsTimer.Stop();
        if (!IsVisible || _pending || !SearchPanel.IsVisible || _iconQuery is null || Results.ActualHeight <= 0) return;
        var visible = new HashSet<SearchRow>();
        if (FindChild<VirtualizingStackPanel>(Results) is { } panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is not ListBoxItem { DataContext: SearchRow row } item || !item.IsVisible) continue;
                var top = item.TranslatePoint(new Point(), Results).Y;
                if (top < Results.ActualHeight && top + item.ActualHeight > 0) visible.Add(row);
            }
        }
        foreach (var pair in _visibleIconLoads.ToArray())
        {
            if (visible.Contains(pair.Key)) continue;
            _visibleIconLoads.Remove(pair.Key);
            pair.Value.Cancel();
        }
        // Result pages retain their row models. Release off-screen bitmaps so
        // paging through many images cannot bypass the bounded image cache.
        foreach (var row in _visibleIconRows)
        {
            if (visible.Contains(row)) continue;
            row.Icon = _icons.Fallback(row.Hit);
            row.IconLoaded = false;
        }
        _visibleIconRows = visible;
        foreach (var row in visible)
        {
            if (row.IconLoaded || _visibleIconLoads.ContainsKey(row)) continue;
            var request = CancellationTokenSource.CreateLinkedTokenSource(_iconQuery.Token);
            _visibleIconLoads.Add(row, request);
            _ = LoadVisibleIconAsync(row, request);
        }
    }

    private async Task LoadVisibleIconAsync(SearchRow row, CancellationTokenSource request)
    {
        try
        {
            await _icons.LoadAsync(row, request.Token);
            if (!request.IsCancellationRequested) row.IconLoaded = true;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_visibleIconLoads.TryGetValue(row, out var current) && current == request) _visibleIconLoads.Remove(row);
            request.Dispose();
        }
    }

    private void CancelVisibleIcons()
    {
        _visibleIconsTimer.Stop();
        foreach (var request in _visibleIconLoads.Values.ToArray()) request.Cancel();
        _visibleIconLoads.Clear();
    }
}
