using System.Windows;
using System.Windows.Automation;
using FilesMate.App.Localization;
using FilesMate.Search;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    public static readonly DependencyProperty ActiveQueryProperty = DependencyProperty.Register(
        nameof(ActiveQuery), typeof(string), typeof(PaletteWindow), new PropertyMetadata(string.Empty));
    public string ActiveQuery { get => (string)GetValue(ActiveQueryProperty); private set => SetValue(ActiveQueryProperty, value); }
    private bool _presentationFailed;
    private bool _presentationCompleted;
    private bool HasCurrentSearchResults => SearchPresentationPolicy.HasCurrentResults(
        _searchGeneration, _acceptedSearchGeneration, _pending, _composing);
    private SearchPresentationState _presentationState;

    private void RefreshSearchPresentation()
    {
        var pageVisible = SearchHeader.Visibility == Visibility.Visible && SettingsPanel.Visibility != Visibility.Visible
            && RankingPanel.Visibility != Visibility.Visible && CategoriesPanel.Visibility != Visibility.Visible && HiddenResultsPanel.Visibility != Visibility.Visible;
        if (!pageVisible) { SearchPanel.Visibility = Visibility.Collapsed; return; }
        var state = SearchPresentationPolicy.Resolve(QueryBox.Text,
            !_presentationFailed && (_pending || _initialSourcesPending),
            HasCurrentSearchResults, _rows.Count);
        SearchPanel.Visibility = Visibility.Visible;
        FilterBar.Visibility = state == SearchPresentationState.Default ? Visibility.Collapsed : Visibility.Visible;
        // Previous-generation rows remain stored until a batch arrives, but they
        // are not presented as actionable results for a different query.
        ResultBody.Visibility = state == SearchPresentationState.Results ? Visibility.Visible : Visibility.Collapsed;
        Results.IsHitTestVisible = state == SearchPresentationState.Results;
        var notice = StatusText.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(StatusText.Text);
        var empty = state == SearchPresentationState.EmptyOrError && !_presentationFailed;
        SearchEmptyCard.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchEmptyHint.Text = StatusText.Text;
        SearchProgressNote.Visibility = state == SearchPresentationState.Results && _initialSourcesPending && !_presentationFailed
            ? Visibility.Visible : Visibility.Collapsed;
        SearchStateCard.Visibility = state == SearchPresentationState.Searching || _presentationFailed
            || (state is SearchPresentationState.Results or SearchPresentationState.Default) && notice ? Visibility.Visible : Visibility.Collapsed;
        SearchStateTitle.Visibility = state == SearchPresentationState.Searching || _presentationFailed
            ? Visibility.Visible : Visibility.Collapsed;
        SearchStateTitle.Text = state == SearchPresentationState.Searching ? StringTable.Get("Search_Looking")
            : StringTable.Get(_presentationFailed ? "Search_StateError" : "Search_StateEmpty");
        // The title already explains the in-progress state. Avoid repeating it.
        if (state == SearchPresentationState.Searching && StatusText.Text == StringTable.Get("Search_Looking"))
            StatusText.Visibility = Visibility.Collapsed;
        SearchKeyboardHints.Visibility = Visibility.Visible;
        SearchKeyboardHintsText.Text = StringTable.Get(state == SearchPresentationState.Default ? "Search_DefaultHint"
            : empty ? "Search_EmptyKeyboardHints" : "Search_KeyboardHints");
        SearchFooterStatus.Text = state == SearchPresentationState.Default ? StringTable.Get("Search_DefaultKeyboardHint")
            : SearchPresentationPolicy.ShowCompleted(state, _presentationCompleted,
                _initialSourcesPending || _loadingMore, _presentationFailed) ? StringTable.Get("Search_Completed") : string.Empty;
        SearchRetryButton.Visibility = _presentationFailed ? Visibility.Visible : Visibility.Collapsed;
        SearchFailureIcon.Visibility = _presentationFailed ? Visibility.Visible : Visibility.Collapsed;
        MoreResultsButton.Visibility = state == SearchPresentationState.Results && (_hasMore || _rows.Count >= 40)
            ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(SearchStateCard, SearchStateTitle.Text);
        AutomationProperties.SetItemStatus(Results, state.ToString());
        AutomationProperties.SetItemStatus(QueryBox, state.ToString());
        if (state != _presentationState)
        {
            _presentationState = state;
            AnimateSection(empty ? SearchEmptyCard : state == SearchPresentationState.Default ? SearchKeyboardHints : SearchStateCard);
        }
        QueueCenter();
    }

    private void ClearSearchFilters_Click(object sender, RoutedEventArgs args)
    {
        var category = _categories.FirstOrDefault(value => value.Visible && value.Builtin == SearchFilter.All)
            ?? _categories.First(value => value.Visible);
        _categoryId = category.Id;
        _filter = category.Builtin ?? SearchFilter.All;
        _offset = 0; _hasMore = false;
        RenderCategoryButtons();
        Search();
        QueryBox.Focus();
    }

    private void EditSearchQuery_Click(object sender, RoutedEventArgs args) { QueryBox.Focus(); QueryBox.SelectAll(); }
    private void RetrySearch_Click(object sender, RoutedEventArgs args) { Search(); QueryBox.Focus(); }
}
