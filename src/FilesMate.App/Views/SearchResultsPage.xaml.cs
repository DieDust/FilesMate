using FilesMate.App.Commands;
using FilesMate.App.Models;
using FilesMate.App.Controls.FileSurface;
using FilesMate.Core.Entries;
using FilesMate.Core.Directories;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Processes;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Views;

public sealed class SearchResultRow(AdvancedSearchHit hit, int index)
{
    public AdvancedSearchHit Hit { get; } = hit;
    public string Name => Hit.Name;
    public string Path => Hit.Application?.LocationPath ?? Hit.Path;
    public string Folder => Hit.Application is not null ? Loc.Get("Application") : System.IO.Path.GetDirectoryName(Hit.Path) ?? Hit.Path;
    public string Glyph => Hit.Application is not null ? "\uE71D" : Hit.IsDirectory ? "\uE8B7" : "\uE8A5";
    public Visibility Stripe => index % 2 == 1 ? Visibility.Visible : Visibility.Collapsed;
    public string Modified => Hit.ModifiedUtcTicks is > 0 and <= 3155378975999999999
        ? new DateTime(Hit.ModifiedUtcTicks.Value, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
    public string Size => Hit.Size is { } n ? n >= 1073741824 ? $"{n / 1073741824d:0.#} GB" : n >= 1048576 ? $"{n / 1048576d:0.#} MB"
        : n >= 1024 ? $"{n / 1024d:0.#} KB" : $"{n} B" : "—";
}

public sealed partial class SearchResultsPage : Page, IDisposable
{
    private sealed record CategoryOption(SearchCategory Category, string Label);
    private readonly string _profile = System.IO.Path.GetDirectoryName(Program.SettingsPath(Localization.LanguageSettings.DefaultFilePath))!;
    private static readonly FilesMate.SearchHost.WindowsApplicationCatalog Catalog = new(() => Environment.ProcessPath ?? "");
    private readonly AdvancedSearchProvider _provider;
    private CancellationTokenSource? _query;
    private bool _ready, _disposed, _hasMore;
    private int _version;
    private long? _totalCount;
    private EntryStore _resultStore = new();
    private SearchRankingPreferencesWatcher? _sortWatcher;
    private string _sharedOrder = "";
    private const int PageSize = 200;
    private string _summary = "";
    private SearchResultRow[] _rows = [];
    private long _generation;
    internal IReadOnlyList<SearchResultRow> ResultRows => _rows;
    public SearchPageRequest Request { get; private set; }
    public event EventHandler? RequestChanged;
    internal bool IsSearching => _query is not null;
    internal int ResultCount => _rows.Length;

    public SearchResultsPage(SearchPageRequest request)
    {
        Request = request = request with { Sort = SearchSortConfiguration.Load(_profile) };
        _provider = new(Catalog, _profile);
        InitializeComponent();
        InitializeFilterControls();
        FlyoutTheme.FollowHost(FilterFlyout);
        QueryBox.PlaceholderText = Loc.Get("SearchPage_QueryHint");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QueryBox, Loc.Get("SearchPage_QueryHint"));
        SearchButton.Content = Loc.Get("SearchPage_Search");
        FilterHeading.Text = Loc.Get("SearchPage_MatchingOptions");
        FilterLabel.Text = Loc.Get("SearchPage_Filters");
        ScopeBox.PlaceholderText = Loc.Get("SearchPage_AllLocations");
        ToolTipService.SetToolTip(ScopeBox, Loc.Get("SearchPage_Scope"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ScopeBox, Loc.Get("SearchPage_Scope"));
        ExtensionsBox.Header = Loc.Get("SearchPage_Extensions"); ExtensionsBox.PlaceholderText = "pdf;docx;txt";
        SizeBox.PlaceholderText = Loc.Get("SearchPage_Size");
        ToolTipService.SetToolTip(SizeBox, Loc.Get("SearchPage_Size") + ": >100mb / 10mb..50mb");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SizeBox, Loc.Get("SearchPage_Size"));
        DateBox.Header = Loc.Get("SearchPage_Modified");
        ToolTipService.SetToolTip(DateBox, "today / 7days / 2026-01-01..2026-12-31");
        CaseBox.Content = Loc.Get("SearchPage_Case"); PathBox.Content = Loc.Get("SearchPage_MatchPath"); RegexBox.Content = Loc.Get("SearchPage_Regex");
        ResetButton.Content = Loc.Get("SearchPage_Reset"); ApplyButton.Content = Loc.Get("SearchPage_Apply");
        ToolTipService.SetToolTip(IndexButton, Loc.Get("SearchPage_Index"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(IndexButton, Loc.Get("SearchPage_Index"));
        StopButton.Content = Loc.Get("SearchPage_Stop");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CategoryBox, Loc.Get("SearchPage_Category"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SortBox, Loc.Get("SearchPage_Sort"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HelpButton, Loc.Get("SearchPage_Help"));
        foreach (var key in new[] { "SortPriority", "NameAZ", "NameZA", "SortNewest", "SortLargest", "ByPath" }) SortBox.Items.Add(Loc.Get("SearchPage_" + key));
        QueryBox.Text = request.Query; ScopeBox.Text = request.Scope ?? ""; ExtensionsBox.Text = request.Extensions;
        SizeBox.Text = request.Size; DateBox.Text = request.Modified; CaseBox.IsChecked = request.MatchCase;
        PathBox.IsChecked = request.MatchPath; RegexBox.IsChecked = request.Regex; SortBox.SelectedIndex = (int)request.Sort;
        SyncVisualFilters();
        InitializeFileSurface();
        Results.NearEndReached += async (_, _) => await LoadMoreAsync();
        Loaded += Page_Loaded;
        Unloaded += (_, _) => { HideFilterPopups(); Cancel(); _sortWatcher?.Dispose(); _sortWatcher = null; Clipboard.ContentChanged -= Clipboard_Changed; _quickPreview?.Close(); _shelfFlyout?.Hide(); };
    }

    private void LoadCategories()
    {
        var selected = (CategoryBox.SelectedItem as CategoryOption)?.Category.Id ?? Request.CategoryId;
        _ready = false;
        var enabled = SearchExecutableConfiguration.Load(_profile);
        var categories = SearchCategories.Load(_profile).Where(c => c.Visible && (enabled || c.Builtin != SearchFilter.Executables))
            .Select(c => new CategoryOption(c, c.Builtin switch
            {
                SearchFilter.All => Loc.Get("SearchPage_AllTypes"), SearchFilter.Apps => Loc.Get("Application"),
                SearchFilter.Executables => Loc.Get("SearchRankExecutable"), SearchFilter.Documents => Loc.Get("SearchRankDocument"),
                SearchFilter.Images => Loc.Get("SearchRankImage"), SearchFilter.Media => Loc.Get("Category_Media"),
                SearchFilter.Folders => Loc.Get("SearchRankFolder"), _ => c.Name,
            })).ToList();
        CategoryBox.ItemsSource = categories;
        CategoryBox.SelectedItem = categories.FirstOrDefault(c => c.Category.Id == selected) ?? categories.FirstOrDefault();
        _ready = true;
    }
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        RefreshNavigationToggle();
        Clipboard.ContentChanged -= Clipboard_Changed; Clipboard.ContentChanged += Clipboard_Changed;
        LoadCategories();
        _sortWatcher ??= new SearchRankingPreferencesWatcher(System.IO.Path.Combine(_profile, "search-index.json"),
            () => DispatcherQueue.TryEnqueue(RefreshSharedOrder));
        StopButton.Visibility = Visibility.Collapsed;
        if (_rows.Length == 0)
        {
            _sharedOrder = SharedOrderSignature();
            _ready = false; SortBox.SelectedIndex = (int)SearchSortConfiguration.Load(_profile); _ready = true;
            await SearchAsync();
        }
        else RefreshSharedOrder();
    }
    private SearchPageRequest ReadRequest() => new(QueryBox.Text.Trim(), string.IsNullOrWhiteSpace(ScopeBox.Text) ? null : ScopeBox.Text.Trim(),
        (CategoryBox.SelectedItem as CategoryOption)?.Category.Id ?? "All", ExtensionsBox.Text.Trim(), SizeBox.Text.Trim(), DateBox.Text.Trim(),
        CaseBox.IsChecked == true, PathBox.IsChecked == true, RegexBox.IsChecked == true, (SearchResultSort)Math.Max(0, SortBox.SelectedIndex));

    internal Task LoadMoreAsync() => SearchAsync(reset: false, append: true);

    internal async Task SearchAsync(bool reset = true, bool debounce = false, bool append = false)
    {
        if (!_ready || _disposed || !IsLoaded || append && (!_hasMore || _query is not null || Results.IsMemoryReclamationBusy)) return;
        Cancel();
        var version = _version;
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(15)); _query = request;
        if (!append)
        {
            Request = ReadRequest(); RequestChanged?.Invoke(this, EventArgs.Empty);
            UpdateFilterSummary();
            BindRows([]); _hasMore = false; _totalCount = null;
            EmptyPanel.Visibility = Visibility.Visible; EmptyText.Text = Loc.Get("Search_Searching");
        }
        StatusLabel.Text = append ? Loc.Format("SearchPage_LoadingMore", _rows.Length) : Loc.Get("Search_Searching");
        StopButton.Visibility = Visibility.Visible;
        try
        {
            if (debounce) await Task.Delay(250, request.Token);
            var database = App.SearchIndex?.FilePath ?? GlobalSearchConfiguration.ResolveDatabase(_profile);
            var offset = append ? _rows.Length : 0;
            var response = await _provider.SearchAsync(database, Request, offset, request.Token, PageSize);
            if (_disposed || version != _version) return;
            var incoming = response.Hits.Select((h, i) => new SearchResultRow(h, offset + i)).ToArray();
            BindRows(append ? [.. _rows, .. incoming] : incoming, append);
            _hasMore = response.HasMore && incoming.Length > 0;
            _totalCount = response.TotalCount ?? _totalCount;
            EmptyPanel.Visibility = _rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var notice = response.Notice is null ? null : Loc.Get(response.Notice);
            EmptyText.Text = notice ?? Loc.Get("SearchPage_NoResults");
            _summary = _totalCount is { } total
                ? Loc.Format(_hasMore ? "SearchPage_TotalLoaded" : "SearchPage_Total", total, _rows.Length)
                : Loc.Format("SearchPage_Loaded", _rows.Length);
            if (notice is not null) _summary += " · " + notice;
            UpdateStatusSelection();
        }
        catch (OperationCanceledException)
        { if (version == _version) ShowError(Loc.Get("SearchPage_TimedOut")); }
        catch (Exception error)
        {
            if (version == _version)
            { ShowError(error is ArgumentException ? Loc.Get(error.Message) : Loc.Get("Search_Unavailable")); App.LogFailure("SearchPage", error); }
        }
        finally
        {
            if (ReferenceEquals(_query, request))
            { _query = null; StopButton.Visibility = Visibility.Collapsed; }
            request.Dispose();
        }
    }

    private void UpdateStatusSelection() => StatusLabel.Text = _summary + (Results.Selection.Count > 0
        ? " · " + Loc.Format("SearchPage_Selected", Results.Selection.Count) : "");

    private string SharedOrderSignature() => SearchSortConfiguration.Load(_profile) + ":" +
        string.Join(",", SearchRankingConfiguration.Load(_profile)) + ":" + SearchExecutableConfiguration.Load(_profile);

    private async void RefreshSharedOrder()
    {
        if (_disposed || !IsLoaded) return;
        var signature = SharedOrderSignature();
        if (signature == _sharedOrder) return;
        _sharedOrder = signature;
        _ready = false; SortBox.SelectedIndex = (int)SearchSortConfiguration.Load(_profile); _ready = true;
        await SearchAsync();
    }

    private async void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        try
        {
            SearchSortConfiguration.Save((SearchResultSort)Math.Max(0, SortBox.SelectedIndex), _profile);
            _sharedOrder = SharedOrderSignature();
            await SearchAsync();
        }
        catch (Exception error) { ShowError(Loc.Get("Order_SaveFailed") + error.Message); }
    }
    private void Cancel() { ++_version; _query?.Cancel(); _query = null; }
    public void Dispose()
    { _disposed = true; Cancel(); _sortWatcher?.Dispose(); Clipboard.ContentChanged -= Clipboard_Changed; _quickPreview?.Close(); _shelfFlyout?.Hide(); Results.ReleaseResources(); _rows = []; }
    private void ShowError(string text) { StatusLabel.Text = text; EmptyText.Text = text; EmptyPanel.Visibility = ResultCount == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        HideFilterPopups();
        await SearchAsync();
    }
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AdvancedScroller is null) return;
        AdvancedScroller.MaxHeight = Math.Clamp(e.NewSize.Height - 110, 180, 420);
        var compact = e.NewSize.Width < 570;
        FilterBar.RowSpacing = compact ? 4 : 0;
        ScopeButton.MaxWidth = e.NewSize.Width < 700 ? 120 : 168;
        ScopeCaption.MaxWidth = e.NewSize.Width < 700 ? 66 : 114;
        TypeCaption.MaxWidth = e.NewSize.Width < 700 ? 64 : 84;
        SizeCaption.MaxWidth = e.NewSize.Width < 700 ? 70 : 110;
        DateCaption.MaxWidth = e.NewSize.Width < 700 ? 70 : 92;
        SortBox.Width = e.NewSize.Width < 650 ? 108 : 126;
        Grid.SetRow(SortBox, compact ? 1 : 0); Grid.SetColumn(SortBox, compact ? 0 : 5);
        Grid.SetColumnSpan(SortBox, compact ? 2 : 1);
        SortBox.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Grid.SetRow(AdvancedButton, compact ? 1 : 0); Grid.SetColumn(AdvancedButton, compact ? 2 : 6);
    }
    private void UpdateFilterSummary()
    {
        UpdateFilterCaptions();
        var conditions = new List<string>();
        if (Request.MatchCase) conditions.Add(Loc.Get("SearchPage_Case"));
        if (Request.MatchPath) conditions.Add(Loc.Get("SearchPage_MatchPath"));
        if (Request.Regex) conditions.Add(Loc.Get("SearchPage_Regex"));
        FilterLabel.Text = Loc.Get("SearchPage_Advanced") + (conditions.Count == 0 ? "" : $" · {conditions.Count}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AdvancedButton, FilterLabel.Text);
        ToolTipService.SetToolTip(AdvancedButton, conditions.Count == 0 ? Loc.Get("SearchPage_Advanced") : string.Join("\n", conditions));
        AdvancedButton.Style = (Style)Resources[conditions.Count > 0 ? "ActiveSearchFilterStyle" : "SearchFilterButtonStyle"];
    }
    private async void Query_KeyDown(object sender, KeyRoutedEventArgs e)
    { if (e.Key == VirtualKey.Enter) { e.Handled = true; HideFilterPopups(); await SearchAsync(); } }
    private async void Query_Changed(object sender, TextChangedEventArgs e) { if (_ready) await SearchAsync(debounce: true); }
    private async void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) await SearchAsync(); }
    private async void Reset_Click(object sender, RoutedEventArgs e)
    { _ready = false; ScopeBox.Text = ExtensionsBox.Text = SizeBox.Text = DateBox.Text = ""; CaseBox.IsChecked = PathBox.IsChecked = RegexBox.IsChecked = false; CategoryBox.SelectedIndex = 0; SyncVisualFilters(); _ready = true; HideFilterPopups(); await SearchAsync(); }
    private void Index_Click(object sender, RoutedEventArgs e) => App.WindowForElement(this)?.OpenSettings("search");
    private void Stop_Click(object sender, RoutedEventArgs e)
    { Cancel(); StopButton.Visibility = Visibility.Collapsed; ShowError(Loc.Get("SearchPage_Cancelled")); }
    private async void Help_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = Loc.Get("SearchPage_Help"), Content = new TextBlock
        { Text = Loc.Get("SearchPage_Examples"), TextWrapping = TextWrapping.Wrap }, CloseButtonText = Loc.Get("Close") };
        ContentDialogTheme.Apply(dialog, this); await dialog.ShowAsync();
    }

    private async void CommonFilter_LostFocus(object sender, RoutedEventArgs e)
    { if (_ready && ReadRequest() != Request) await SearchAsync(); }
    internal void RefreshNavigationToggle() => App.WindowForElement(this)?.PaintNavigationToggle(PaneToggle, PaneToggleIcon);
    private void PaneToggle_Click(object sender, RoutedEventArgs e) => App.WindowForElement(this)?.ToggleNavigation();

    private void BindRows(SearchResultRow[] rows, bool append = false)
    {
        var previousCount = append ? _rows.Length : 0;
        if (!append) { _resultStore = new EntryStore(); _tags.Clear(); ++_generation; }
        _rows = rows;
        _resultStore.Append(rows.Skip(previousCount).Select((row, i) => new FileEntryCore(previousCount + i, row.Name, (ulong)Math.Max(0, row.Hit.Size ?? 0),
            row.Hit.ModifiedUtcTicks ?? 0, 0, row.Hit.IsDirectory ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Normal,
            row.Hit.IsDirectory ? EntryKind.Directory : EntryKind.File)).ToArray());
        var scrollOffset = Results.ScrollOffset;
        Results.Bind(_resultStore, EntryViewIndex.InSourceOrder(_resultStore, _generation, SurfaceSort()), _generation, append);
        if (append) Results.RestoreScrollOffset(scrollOffset);
        Results.SetSort(SurfaceSort()); SyncCommands();
        if (rows.Length > 0) _ = LoadTagsAsync();
    }

    private EntrySort SurfaceSort() => Request.Sort switch
    {
        SearchResultSort.NameDescending => EntrySort.Name with { Ascending = false },
        SearchResultSort.SizeDescending => EntrySort.Size with { Ascending = false },
        SearchResultSort.ModifiedDescending => EntrySort.Modified with { Ascending = false },
        SearchResultSort.Path => new EntrySort { Column = EntrySortColumn.Location },
        _ => EntrySort.Name
    };
    private void SortResults(EntrySortColumn column) => SortBox.SelectedIndex = (int)(column switch
    {
        EntrySortColumn.Name => Request.Sort == SearchResultSort.Name ? SearchResultSort.NameDescending : SearchResultSort.Name,
        EntrySortColumn.Size => SearchResultSort.SizeDescending,
        EntrySortColumn.Modified => SearchResultSort.ModifiedDescending,
        EntrySortColumn.Location or EntrySortColumn.FullPath => SearchResultSort.Path,
        _ => SearchResultSort.Priority
    });
}
