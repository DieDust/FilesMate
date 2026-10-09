using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using FilesMate.Search;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private long _searchGeneration;
    private long _acceptedSearchGeneration = -1;
    private bool _initialSourcesPending;
    private SearchSnapshot? _deferredSearchSnapshot;
    private long _mergeWaitGeneration = -1;
    private EventHandler? _searchRenderObserver;
    private long _firstRenderedSearchGeneration = -1;

    private sealed record SearchSnapshot(GlobalSearchResponse Response, bool IsFinal, bool IsIncomplete,
        SearchFilter Filter, long Generation, long StartedTimestamp, bool Append);

    private bool SearchResultInteractionBusy => SearchMergeInteractionPolicy.IsBusy(
        Mouse.LeftButton == MouseButtonState.Pressed, Results.IsMouseOver,
        _pressedRow is not null || _pressedApplication is not null || _collapseSelectionOnUp,
        _dragging, _contextOpen || _resultMenu?.IsOpen == true);

    private void QueueSearchSnapshot(SearchSnapshot snapshot)
    {
        if (snapshot.Generation != _searchGeneration || !IsVisible) return;
        if (SearchResultInteractionBusy)
        {
            // A gesture/menu owns its current targets until it finishes. Keep only
            // the latest replacement, without extending the provider's timeout.
            _deferredSearchSnapshot = snapshot;
            if (_mergeWaitGeneration != snapshot.Generation)
            {
                _mergeWaitGeneration = snapshot.Generation;
                _ = FlushSearchSnapshotAsync(snapshot.Generation);
            }
            return;
        }
        _deferredSearchSnapshot = null;
        ApplySearchSnapshot(snapshot);
    }

    private async Task FlushSearchSnapshotAsync(long generation)
    {
        try
        {
            while (generation == _searchGeneration && IsVisible && _deferredSearchSnapshot is not null && SearchResultInteractionBusy)
                await Task.Delay(25);
            if (generation != _searchGeneration || !IsVisible || _deferredSearchSnapshot is not { } snapshot) return;
            _deferredSearchSnapshot = null;
            ApplySearchSnapshot(snapshot);
        }
        catch (Exception error)
        {
            Trace.WriteLine("FilesMate deferred search merge: " + error);
            if (generation == _searchGeneration && IsVisible)
            {
                SearchFailed(false, Loc.Get("Search_IndexUnavailable"));
                _pending = false;
            }
        }
        finally { if (_mergeWaitGeneration == generation) _mergeWaitGeneration = -1; }
    }

    private void ApplySearchSnapshot(SearchSnapshot snapshot)
    {
        if (snapshot.Generation != _searchGeneration || !IsVisible) return;
        var response = snapshot.Response;
        var incomplete = SearchPresentationPolicy.IncompleteResponse(snapshot.IsIncomplete, response.Notice);
        var focus = CaptureSearchResultFocus();
        var first = _acceptedSearchGeneration != snapshot.Generation;
        if (!snapshot.IsFinal && response.Hits.Count == 0)
        {
            // An empty source is not an empty query. Keep the searching state
            // until a useful current batch or the final response arrives.
            CountLabel.Text = _pending ? Loc.Get("Search_Looking") : Loc.Format("Search_FoundCount", _rows.Count);
            return;
        }
        var scroll = FindChild<ScrollViewer>(Results);
        var scrollOffset = scroll?.VerticalOffset ?? 0;
        _suppressPreviewSelection = true;
        try
        {
            if (snapshot.Append)
            {
                foreach (var hit in response.Hits.Where(hit => _resultIdentities.Add(SearchHitIdentity.From(hit))))
                    _rows.Add(new SearchRow(hit, _icons.Fallback(hit)));
            }
            else
            {
                var selected = SelectedRows().Select(row => SearchHitIdentity.From(row.Hit)).ToHashSet();
                var previous = _rows.ToDictionary(row => SearchHitIdentity.From(row.Hit));
                _resultIdentities.Clear();
                var desired = response.Hits.Where(hit => _resultIdentities.Add(SearchHitIdentity.From(hit))).Select(hit =>
                    previous.TryGetValue(SearchHitIdentity.From(hit), out var row) && row.Hit == hit ? row : new SearchRow(hit, _icons.Fallback(hit))).ToArray();
                var retained = desired.ToHashSet();
                if (_previewRow is not null && !retained.Contains(_previewRow)) ClearPreview();
                for (var i = _rows.Count - 1; i >= 0; i--)
                    if (!retained.Contains(_rows[i])) _rows.RemoveAt(i);
                for (var i = 0; i < desired.Length; i++)
                {
                    if (i < _rows.Count && ReferenceEquals(_rows[i], desired[i])) continue;
                    var oldIndex = _rows.IndexOf(desired[i]);
                    if (oldIndex >= 0) _rows.Move(oldIndex, i); else _rows.Insert(i, desired[i]);
                }
                var retainedSelection = SearchPresentationPolicy.RetainSelection(
                    desired.Select(row => SearchHitIdentity.From(row.Hit)).ToArray(), selected, first).ToHashSet();
                RestoreResultSelection(retainedSelection);
                if (first && Results.SelectedItem is not null) Results.ScrollIntoView(Results.SelectedItem);
            }
            if (scroll is not null && (!first || snapshot.Append)) scroll.ScrollToVerticalOffset(scrollOffset);
        }
        finally { _suppressPreviewSelection = false; }

        _acceptedSearchGeneration = snapshot.Generation;
        _presentationFailed = SearchPresentationPolicy.FailedCompletion(snapshot.IsFinal, incomplete);
        _presentationCompleted = SearchPresentationPolicy.SuccessfulCompletion(snapshot.IsFinal, incomplete);
        _pending = false; // Current-generation results are actionable while another source is loading.
        if (snapshot.IsFinal)
        {
            _offset = snapshot.Append ? _offset + response.Hits.Count : response.Hits.Count;
            _hasMore = !incomplete && response.HasMore && response.Hits.Count > 0;
            _initialSourcesPending = false;
        }
        CountLabel.Text = !snapshot.IsFinal ? Loc.Format("Search_FoundCount", _rows.Count)
            : _rows.Count > 0 ? Loc.Format(incomplete ? "SearchPage_Loaded" : _hasMore ? "Search_MoreCount" : "Search_ResultCount", _rows.Count)
            : _presentationFailed ? "" : Loc.Format("Search_ResultCount", 0);
        var notice = response.Notice is { } key
            ? key.StartsWith("SearchPage_", StringComparison.Ordinal) || key.StartsWith("Search_", StringComparison.Ordinal) ? Loc.Get(key) : key
            : null;
        StatusText.Text = notice ?? (_presentationFailed ? Loc.Get(_rows.Count > 0 ? "Search_PartialUnavailable" : "Search_IndexUnavailable")
            : !snapshot.IsFinal ? _rows.Count == 0 ? Loc.Get("Search_Looking") : ""
            : _rows.Count == 0 ? Loc.Get("Search_EmptyHint") : "");
        StatusText.Visibility = StatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        IndexHelpButton.Visibility = snapshot.IsFinal && (notice is not null || _rows.Count == 0 && snapshot.Filter == SearchFilter.All)
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshSearchPresentation();
        if (!snapshot.Append) RestoreSearchResultFocus(focus, snapshot.Generation);
        if (!snapshot.Append) AnimateSearchLayout();
        Center();
        QueueVisibleIcons();
        TraceSearchStage(snapshot.Generation, snapshot.StartedTimestamp, snapshot.IsFinal ? "final-applied" : "partial-applied", _rows.Count);
        ObserveSearchRender(snapshot);
    }

    private sealed record SearchResultFocus(SearchHitIdentity Identity, string? ActionName, bool Checkbox);

    private SearchResultFocus? CaptureSearchResultFocus()
    {
        if (!Results.IsKeyboardFocusWithin || Keyboard.FocusedElement is not DependencyObject focused
            || Ancestor<ListBoxItem>(focused)?.DataContext is not SearchRow row || !_rows.Contains(row)) return null;
        var action = Ancestor<ButtonBase>(focused);
        return new(SearchHitIdentity.From(row.Hit), action?.Name, action is CheckBox);
    }

    private void RestoreSearchResultFocus(SearchResultFocus? focus, long generation)
    {
        if (focus is null || CaptureSearchResultFocus() == focus) return;
        // Do not steal focus from input or another command if the user moves it
        // before the deferred containers are ready. Never force UpdateLayout here.
        var fallbackFocus = Keyboard.FocusedElement;
        var target = _rows.FirstOrDefault(row => SearchHitIdentity.From(row.Hit) == focus.Identity);
        if (target is null) { QueryBox.Focus(); return; }
        Results.ScrollIntoView(target);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (generation != _searchGeneration || !IsVisible || !HasCurrentSearchResults
                || !ReferenceEquals(Keyboard.FocusedElement, fallbackFocus)) return;
            // Resolve again: another batch may have updated the row while queued.
            var current = _rows.FirstOrDefault(row => SearchHitIdentity.From(row.Hit) == focus.Identity);
            if (current is null) { QueryBox.Focus(); return; }
            if (Results.ItemContainerGenerator.ContainerFromItem(current) is not ListBoxItem item) return;
            var selected = SelectedRows().Select(row => SearchHitIdentity.From(row.Hit)).ToHashSet();
            var suppressed = _suppressPreviewSelection;
            _suppressPreviewSelection = true;
            try
            {
                var action = focus.Checkbox ? FindChild<CheckBox>(item)
                    : string.IsNullOrEmpty(focus.ActionName) ? null : FindResultAction(item, focus.ActionName);
                if (action is not { IsVisible: true, IsEnabled: true } || !action.Focus()) item.Focus();
            }
            finally
            {
                // WPF keyboard focus may implicitly select one item. Restore
                // the identities chosen by the user, including multiselection.
                RestoreResultSelection(selected);
                _suppressPreviewSelection = suppressed;
            }
        }));
    }

    private void RestoreResultSelection(IReadOnlySet<SearchHitIdentity> identities)
    {
        var desired = _rows.Where(row => identities.Contains(SearchHitIdentity.From(row.Hit))).ToHashSet();
        foreach (var selected in Results.SelectedItems.Cast<SearchRow>().ToArray())
            if (!desired.Contains(selected)) Results.SelectedItems.Remove(selected);
        foreach (var row in _rows)
            if (desired.Contains(row) && !Results.SelectedItems.Contains(row)) Results.SelectedItems.Add(row);
    }

    private static ButtonBase? FindResultAction(DependencyObject parent, string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ButtonBase button && button.Name == name) return button;
            if (FindResultAction(child, name) is { } nested) return nested;
        }
        return null;
    }

    private static void TraceSearchStage(long generation, long started, string phase, int count = 0)
        => Trace.WriteLine($"FilesMate search generation={generation} stage={phase} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2} rows={count}");

    private static void TraceSearchSources(long generation, long started, GlobalSearchUpdate update)
    {
        var timing = update.Timings;
        TraceSearchStage(generation, started, update.IsFinal ? "final-ready" : "partial-ready", update.Response.Hits.Count);
        Trace.WriteLine($"FilesMate search generation={generation} providerStart={timing.StartedTimestamp} appsStart={timing.ApplicationsStartedTimestamp} appsEnd={timing.ApplicationsCompletedTimestamp} filesStart={timing.FilesStartedTimestamp} filesEnd={timing.FilesCompletedTimestamp}");
    }

    private void ObserveSearchRender(SearchSnapshot snapshot)
    {
        CancelSearchRenderObservation();
        _searchRenderObserver = (_, _) =>
        {
            if (snapshot.Generation != _searchGeneration || !IsVisible) { CancelSearchRenderObservation(); return; }
            if (_rows.Count > 0 && !HasVisibleSearchResultContainer()) return;
            CancelSearchRenderObservation();
            if (_rows.Count > 0 && _firstRenderedSearchGeneration != snapshot.Generation)
            {
                _firstRenderedSearchGeneration = snapshot.Generation;
                TraceSearchStage(snapshot.Generation, snapshot.StartedTimestamp, "first-visible-actionable", _rows.Count);
            }
            if (snapshot.IsFinal) TraceSearchStage(snapshot.Generation, snapshot.StartedTimestamp, snapshot.Append ? "page-visible" : "final-visible", _rows.Count);
        };
        CompositionTarget.Rendering += _searchRenderObserver;
    }

    private void CancelSearchRenderObservation()
    {
        if (_searchRenderObserver is null) return;
        CompositionTarget.Rendering -= _searchRenderObserver;
        _searchRenderObserver = null;
    }

    private bool HasVisibleSearchResultContainer()
    {
        if (FindChild<VirtualizingStackPanel>(Results) is not { } panel) return false;
        foreach (var child in panel.Children)
        {
            if (child is not ListBoxItem { ActualHeight: > 0, IsVisible: true } item) continue;
            var top = item.TranslatePoint(new Point(), Results).Y;
            if (top < Results.ActualHeight && top + item.ActualHeight > 0) return true;
        }
        return false;
    }
}
