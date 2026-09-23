using FilesMate.App.Animations;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.Foundation;
using Windows.System;

using NavigationPathSegment = FilesMate.App.Navigation.PathSegment;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar : UserControl
{
    public static readonly DependencyProperty ForceCompactNavProperty = DependencyProperty.Register(
        nameof(ForceCompactNav),
        typeof(bool),
        typeof(Omnibar),
        new PropertyMetadata(false, OnForceCompactNavChanged));

    private readonly OmnibarSession _session = new();
    private List<NavigationPathSegment> _segments = [];
    private Brush? _pathBorder;
    private Brush? _searchBorder;
    private bool _editRequestPending;
    private bool _searchFocused;
    private bool _searchHovered;
    private bool _searchOpen;
    private bool _searchRequestPending;
    private bool _searchClosePending;
    private bool _overflowPending;
    private int _searchGeneration;
    private int _searchFocusVersion;
    private CancellationTokenSource? _searchRequest;
    private CancellationTokenSource? _crumbRequest;
    private IReadOnlyList<HomeSearchHit> _hits = [];
    private Storyboard? _crumbFolderStoryboard;
    private const double CrumbFolderSlideOffset = -8;
    private FrameworkElement? _crumbChevron;
    private string? _crumbFlyoutPath;
    private string? _crumbPath;
    private bool _crumbFolderClosing;
    private bool _crumbFolderClosePending;

    public Omnibar()
    {
        InitializeComponent();
        SearchScopeLabel.Text = StringTable.Get("Search_AllLocations");
        SearchStatus.Text = StringTable.Get("Search_TypeToStart");
        AutomationProperties.SetName(ClearSearchButton, StringTable.Get("Search_Clear"));
        ToolTipService.SetToolTip(ClearSearchButton, StringTable.Get("Search_Clear"));
        PathHost.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(PathHost_PointerPressed),
            handledEventsToo: true);
        AutomationProperties.SetName(PathBox, StringTable.Get("Address"));
        AutomationProperties.SetName(SearchBox, StringTable.Get("SearchPlaceholderFolder"));
        ToolTipService.SetToolTip(SearchBox, StringTable.Get("SearchPlaceholderFolder"));
        AutomationProperties.SetName(SearchButton, StringTable.Get("SearchPlaceholderFolder"));
        ToolTipService.SetToolTip(SearchButton, StringTable.Get("SearchPlaceholderFolder"));
        Loaded += (_, _) =>
        {
            _pathBorder = PathHost.BorderBrush;
            _searchBorder = SearchHost.BorderBrush;
            SearchSuggestPopup.PlacementTarget = SearchHost;
            SearchSuggestPopup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedRight;
            UpdateSearchPlaceholder();
            ApplyOverflow();
            ApplyMode(animate: false);
            ApplySearchOpen(animate: false);
#if FILESMATE_UI_TEST
            if (Environment.GetEnvironmentVariable("FILESMATE_BREADCRUMB_SMOKE") == "1") _ = RunBreadcrumbSmokeAsync();
#endif
        };
        Unloaded += (_, _) =>
        {
            CancelSearch();
            DismissCrumbFolders();
        };
    }

    public event RoutedEventHandler? BackClicked;

    public event RoutedEventHandler? ForwardClicked;

    public event RoutedEventHandler? UpClicked;

    public event RoutedEventHandler? RefreshClicked;

    public event EventHandler<string>? PathSubmitted;

    public event EventHandler<string>? CrumbClicked;

    public event EventHandler<string>? SearchChosen;

    public Func<string, IReadOnlyList<HomeSearchHit>>? SearchDeviceFolder { get; set; }

    public event EventHandler? ModeCanceled;

    public Func<long, string?>? ResolveTagName { get; set; }

    public bool ForceCompactNav
    {
        get => (bool)GetValue(ForceCompactNavProperty);
        set => SetValue(ForceCompactNavProperty, value);
    }

    public bool CanGoBack
    {
        get => Toolbar.CanGoBack;
        set => Toolbar.CanGoBack = value;
    }

    public bool CanGoForward
    {
        get => Toolbar.CanGoForward;
        set => Toolbar.CanGoForward = value;
    }

    public bool CanGoUp
    {
        get => Toolbar.CanGoUp;
        set => Toolbar.CanGoUp = value;
    }

    public bool CanRefresh
    {
        get => Toolbar.CanRefresh;
        set => Toolbar.CanRefresh = value;
    }

    public bool IsEditing => _session.Mode == OmnibarMode.PathEdit;
    public string DraftText => PathBox.Text;

    public string Text
    {
        get => _session.Path;
        set
        {
            if (!string.Equals(_session.Path, value, StringComparison.OrdinalIgnoreCase))
            {
                CancelSearch();
                SearchEverywhere.IsChecked = false;
                SearchBox.Text = string.Empty;
                DismissCrumbFolders();
            }

            _session.SetPath(value);
            UpdateSearchPlaceholder();
            if (_session.Mode != OmnibarMode.PathEdit)
            {
                PathBox.Text = _session.Path;
                RebuildCrumbs(_session.Path);
                ClearPathErrorVisual();
            }
        }
    }

    public void BeginPathEdit()
    {
        DismissCrumbFolders();
        _session.BeginPathEdit();
        PathBox.Text = _session.Draft;
        // Editing is a mode switch, not content navigation. Apply it atomically
        // so focus cannot land on the outgoing breadcrumb tree for one frame.
        ApplyMode(animate: false);
        PathBox.Focus(FocusState.Programmatic);
        PathBox.SelectAll();
    }

    public void BeginSearch()
    {
        ++_searchFocusVersion;
        _searchOpen = true;
        ApplySearchOpen(animate: false);
        FocusSearchBox();
    }

    public void DismissSearch()
    {
        if (_searchOpen)
        {
            RequestSearchClose();
        }
    }

    public bool IsSearchOpen => _searchOpen;

    public bool IsSearchSource(DependencyObject source) =>
        IsInside(source, SearchCluster) || IsInside(source, SearchPanel);

    public bool IsCrumbFolderSource(DependencyObject source) =>
        (_crumbChevron is not null && IsInside(source, _crumbChevron))
        || IsInside(source, CrumbFolderHost);

    public void DismissCrumbFolders()
    {
        _crumbRequest?.Cancel();
        if (CrumbFolderPopup.IsOpen && !_crumbFolderClosing)
        {
            RequestCrumbFolderClose();
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => RequestSearch();

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        BeginSearch();
    }

    private void RequestSearch()
    {
        if (_searchRequestPending)
        {
            return;
        }

        _searchRequestPending = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _searchRequestPending = false;
                if (IsLoaded)
                {
                    BeginSearch();
                }
            }))
        {
            _searchRequestPending = false;
        }
    }

    private void RequestSearchClose(bool restoreFocus = false)
    {
        if (_searchClosePending || !_searchOpen)
        {
            return;
        }

        _searchClosePending = true;
        var version = _searchFocusVersion;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _searchClosePending = false;
                if (!IsLoaded || version != _searchFocusVersion)
                {
                    return;
                }

                _searchOpen = false;
                _searchFocused = false;
                ApplySearchOpen(animate: true);
                ApplySearchChrome();
                UpdatePlaceholderVisual();
                SearchSuggestPopup.IsOpen = false;
                if (restoreFocus) ModeCanceled?.Invoke(this, EventArgs.Empty);
            }))
        {
            _searchClosePending = false;
        }
    }

    private void ApplySearchOpen(bool animate)
    {
        var visible = _searchOpen || OmnibarSearchLayout.ShowField(ActualWidth);
        SearchBox.IsTabStop = visible;
        SearchBox.IsHitTestVisible = visible;
        VisualStateManager.GoToState(this, visible ? "SearchOpen" : "SearchClosed", useTransitions: false);
        // No dependent Width animation: Ctrl+F must accept the very next key,
        // and the breadcrumb layout should not run again on every animation frame.
        SearchSizer.Width = OmnibarSearchLayout.Width(ActualWidth, _searchOpen);
        UpdatePlaceholderVisual();
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.Select(SearchBox.Text.Length, 0);
    }

    private void SearchSizer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SearchSizer.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height)),
        };
    }

    private void SearchPane_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_searchOpen)
        {
            e.Handled = true;
            RequestSearch();
            return;
        }

        SearchBox.Focus(FocusState.Keyboard);
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        ++_searchFocusVersion;
        _searchOpen = true;
        _searchFocused = true;
        ApplySearchOpen(animate: false);
        ApplySearchChrome();
        UpdatePlaceholderVisual();
        SearchBox.Select(SearchBox.Text.Length, 0);
        ShowSearchStatus(string.IsNullOrWhiteSpace(SearchBox.Text) ? StringTable.Get("Search_TypeToStart") : SearchStatus.Text);
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e) =>
        _ = DispatcherQueue.TryEnqueue(CloseSearchIfFocusLeft);

    private void CloseSearchIfFocusLeft()
    {
        if (XamlRoot is null)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (focused is not null && IsSearchSource(focused))
        {
            return;
        }

        _searchFocused = false;
        ApplySearchChrome();
        UpdatePlaceholderVisual();
        SearchSuggestPopup.IsOpen = false;
        RequestSearchClose();
    }

    private void SearchHost_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _searchHovered = true;
        ApplySearchChrome();
    }

    private void SearchHost_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _searchHovered = false;
        ApplySearchChrome();
    }

    private void ApplySearchChrome()
    {
        if (_searchBorder is null)
        {
            return;
        }

        if (_searchFocused
            && Application.Current.Resources.TryGetValue("FilesMate.Selection.AccentBrush", out var accent)
            && accent is Brush accentBrush)
        {
            SearchHost.BorderBrush = accentBrush;
            return;
        }

        if (_searchHovered
            && Application.Current.Resources.TryGetValue("FilesMate.Glass.BorderStrongBrush", out var strong)
            && strong is Brush strongBrush)
        {
            SearchHost.BorderBrush = strongBrush;
            return;
        }

        SearchHost.BorderBrush = _searchBorder;
    }

    private void UpdateSearchPlaceholder()
    {
        var key = HomeLocation.IsHome(_session.Path) ? "SearchPlaceholderHome" : "SearchPlaceholderFolder";
        var text = StringTable.Get(key);
        SearchPlaceholderLabel.Text = text;
        AutomationProperties.SetName(SearchBox, text);
        AutomationProperties.SetName(SearchButton, text);
        ToolTipService.SetToolTip(SearchBox, text);
        ToolTipService.SetToolTip(SearchButton, text);
        UpdatePlaceholderVisual();
    }

    private void UpdatePlaceholderVisual()
    {
        var show = (_searchOpen || OmnibarSearchLayout.ShowField(ActualWidth))
            && !_searchFocused && string.IsNullOrEmpty(SearchBox.Text);
        SearchPlaceholderLabel.Opacity = show ? 1 : 0;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public bool CancelMode()
    {
        if (!_session.Cancel())
        {
            return false;
        }

        PathBox.Text = _session.Path;
        ClearPathErrorVisual();
        ApplyMode(animate: false);
        ModeCanceled?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ApplyCommittedPath(string path)
    {
        _ = _session.Cancel();
        Text = path;
        ApplyMode(animate: false);
    }

    public bool TryCommitPath(Func<string, string> commit)
    {
        _session.SetDraft(PathBox.Text);
        if (!_session.SubmitPath(commit))
        {
            ShowErrorVisual(_session.PathError);
            ApplyMode(animate: false);
            PathBox.Focus(FocusState.Programmatic);
            return false;
        }

        PathBox.Text = _session.Path;
        RebuildCrumbs(_session.Path);
        ClearPathErrorVisual();
        ApplyMode(animate: false);
        return true;
    }

    private static void OnForceCompactNavChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Omnibar bar)
        {
            bar.ScheduleOverflow();
        }
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleOverflow();

    private void ScheduleOverflow()
    {
        if (_overflowPending)
        {
            return;
        }

        _overflowPending = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyPendingOverflow))
        {
            _overflowPending = false;
        }
    }

    private void ApplyPendingOverflow()
    {
        _overflowPending = false;
        if (IsLoaded)
        {
            ApplyOverflow();
        }
    }

    private void ApplyOverflow()
    {
        if (Toolbar is null)
        {
            return;
        }

        Toolbar.ApplyOverflow(OmnibarOverflow.ForWidth(ActualWidth, ForceCompactNav));
        ApplySearchOpen(animate: false);
    }

    private void ApplyMode(bool animate)
    {
        var duration = App.Motion.Resolve(MotionDurations.Standard);
        var useTransitions = animate && duration > TimeSpan.Zero;
        var state = _session.Mode == OmnibarMode.PathEdit ? "PathEdit" : "PathDisplay";
        VisualStateManager.GoToState(this, state, useTransitions);
        // The full path is already visible while editing. Keeping the display-mode
        // tooltip here only obscures the text and selection under the pointer.
        ToolTipService.SetToolTip(
            PathHost,
            _session.Mode == OmnibarMode.PathEdit ? _session.PathError : _session.Path);
    }

    private void PathEditSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session.Mode != OmnibarMode.PathDisplay)
        {
            return;
        }

        e.Handled = true;
        RequestPathEdit();
    }

    private void PathHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_session.Mode == OmnibarMode.PathDisplay
            && e.OriginalSource is DependencyObject source
            && !IsInside(source, Crumbs))
        {
            RequestPathEdit();
            e.Handled = true;
        }
    }

    private void PathHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session.Mode != OmnibarMode.PathDisplay)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && IsInside(source, Crumbs))
        {
            return;
        }

        e.Handled = true;
        RequestPathEdit();
    }

    private void RequestPathEdit()
    {
        if (_session.Mode != OmnibarMode.PathDisplay || _editRequestPending)
        {
            return;
        }

        _editRequestPending = true;
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (!dispatcher.TryEnqueue(() =>
            {
                _editRequestPending = false;
                if (_session.Mode == OmnibarMode.PathDisplay && IsLoaded)
                {
                    BeginPathEdit();
                }
            }))
        {
            _editRequestPending = false;
        }
    }

    private void Toolbar_BackClicked(object sender, RoutedEventArgs e) => BackClicked?.Invoke(this, e);

    private void Toolbar_ForwardClicked(object sender, RoutedEventArgs e) => ForwardClicked?.Invoke(this, e);

    private void Toolbar_UpClicked(object sender, RoutedEventArgs e) => UpClicked?.Invoke(this, e);

    private void Toolbar_RefreshClicked(object sender, RoutedEventArgs e) => RefreshClicked?.Invoke(this, e);

    private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            PathSubmitted?.Invoke(this, PathBox.Text);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CancelMode();
            e.Handled = true;
        }
    }

    private void PathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Focus can briefly move between TextBox template parts. Check on the
        // next dispatcher turn so only a genuine move outside the editor
        // restores the breadcrumb preview.
        _ = DispatcherQueue.TryEnqueue(CancelPathEditWhenFocusLeaves);
    }

    private void CancelPathEditWhenFocusLeaves()
    {
        if (_session.Mode != OmnibarMode.PathEdit || XamlRoot is null)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (focused is null || !IsInside(focused, PathBox))
        {
            CancelMode();
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await RunSearchAsync();

    private async void SearchEverywhere_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private void ShowSearchStatus(string message)
    {
        SearchStatus.Text = message;
        SearchSuggestPopup.IsOpen = _searchOpen && _searchFocused;
    }

    private async Task RunSearchAsync()
    {
        _session.SetFilter(SearchBox.Text);
        UpdatePlaceholderVisual();
        CancelSearch(hide: false);
        var generation = _searchGeneration;
        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            PublishHits(generation, []);
            return;
        }

        var request = new CancellationTokenSource();
        _searchRequest = request;
        var scope = SearchEverywhere.IsChecked == true || HomeLocation.IsHome(_session.Path) ? null : _session.Path;
        ShowSearchStatus(StringTable.Get("Search_Searching"));
        request.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(0.2), request.Token);
            if (generation != _searchGeneration) return;
            if (scope is not null && FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(scope, out _))
            {
                var hits = SearchDeviceFolder?.Invoke(query) ?? [];
                PublishHits(generation, hits, StringTable.Format("Device_SearchCurrentFolder", hits.Count));
                return;
            }
            if (App.SearchIndex is null)
            {
                PublishHits(generation, [], StringTable.Get("Search_Unavailable"));
                return;
            }

            var rank = App.SearchIndexSettingsStore?.Load().RankOrder;
            var index = App.SearchIndex;
            var response = index is FileNameIndexService service
                ? await service.SearchWithStatusAsync(query, scope, rank, request.Token)
                : new FileNameSearchResponse(await index.SearchAsync(query, scope, rank, request.Token), false);
            var message = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                StringTable.Get(response.Partial ? "Search_Partial" : "Search_Count"), response.Hits.Count);
            PublishHits(generation, response.Hits, message);
        }
        catch (OperationCanceledException)
        {
            if (generation == _searchGeneration) PublishHits(generation, [], StringTable.Get("Search_Timeout"));
        }
        catch (Exception error)
        {
            App.AppendCrashRecord("Search", error);
            PublishHits(generation, [], StringTable.Get("Search_Unavailable"));
        }
        finally
        {
            if (ReferenceEquals(_searchRequest, request))
            {
                _searchRequest = null;
            }

            request.Dispose();
        }
    }

    private void CancelSearch(bool hide = true)
    {
        ++_searchGeneration;
        _searchRequest?.Cancel();
        _hits = [];
        SearchHits.ItemsSource = _hits;
        if (hide) SearchSuggestPopup.IsOpen = false;
    }

    private void PublishHits(int generation, IReadOnlyList<HomeSearchHit> hits, string? message = null)
    {
        if (generation != _searchGeneration)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _searchGeneration)
            {
                return;
            }

            _hits = hits;
            SearchHits.ItemsSource = hits;
            ShowSearchStatus(message ?? StringTable.Get("Search_TypeToStart"));
        });
    }

    private void SearchHits_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSearchHit hit)
        {
            RaiseSearch(hit);
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            var version = _searchFocusVersion;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded || version != _searchFocusVersion) return;
                CancelSearch();
                SearchBox.Text = string.Empty;
                _session.SetFilter(string.Empty);
                RequestSearchClose(restoreFocus: true);
            });
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            if (SearchHits.SelectedItem is HomeSearchHit selected)
            {
                RaiseSearch(selected);
            }
            else if (_hits.Count > 0)
            {
                RaiseSearch(_hits[0]);
            }

            e.Handled = true;
            return;
        }

        if ((e.Key == VirtualKey.Down || e.Key == VirtualKey.Up) && _hits.Count > 0)
        {
            SearchSuggestPopup.IsOpen = true;
            var index = SearchHits.SelectedIndex < 0 ? 0 : Math.Clamp(SearchHits.SelectedIndex + (e.Key == VirtualKey.Down ? 1 : -1), 0, _hits.Count - 1);
            SearchHits.SelectedIndex = index;
            SearchHits.ScrollIntoView(_hits[index]);
            e.Handled = true;
        }
    }

    private void RaiseSearch(HomeSearchHit hit)
    {
        var path = hit.Path;
        if (!string.IsNullOrEmpty(path))
        {
            SearchChosen?.Invoke(this, path);
            SearchSuggestPopup.IsOpen = false;
        }
    }

    private void CrumbName_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && !string.IsNullOrEmpty(path))
        {
            DismissSearch();
            RaiseCrumb(path);
        }
    }

    private void CrumbChevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } chevron || string.IsNullOrEmpty(path))
        {
            return;
        }

        DismissSearch();
        _ = DispatcherQueue.TryEnqueue(() => ShowCrumbFolders(path, chevron));
    }

    private async void ShowCrumbFolders(string path, FrameworkElement chevron)
    {
        if (!IsLoaded || !chevron.IsLoaded || chevron.XamlRoot is null)
        {
            return;
        }

        _crumbRequest?.Cancel();
        if (_crumbFolderClosing)
        {
            _crumbFolderStoryboard?.Stop();
            _crumbFolderClosing = false;
        }

        if (CrumbFolderPopup.IsOpen
            && string.Equals(_crumbFlyoutPath, path, StringComparison.OrdinalIgnoreCase))
        {
            AnimateCrumbFolder(open: false);
            return;
        }

        var request = new CancellationTokenSource();
        _crumbRequest = request;
        IReadOnlyList<NavigationPathSegment> folders;
        try
        {
            folders = await PathChildren.FoldersAsync(path, App.ExplorerPreferences.ShowHiddenFiles, request.Token);
            if (request.IsCancellationRequested || !IsLoaded || !chevron.IsLoaded || chevron.XamlRoot is null)
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_crumbRequest, request))
            {
                _crumbRequest = null;
            }

            request.Dispose();
        }

        if (folders.Count == 0)
        {
            if (CrumbFolderPopup.IsOpen)
            {
                AnimateCrumbFolder(open: false);
            }

            return;
        }

        ShowCrumbFolderItems(folders, chevron, path);
    }

    private void ShowCrumbFolderItems(IReadOnlyList<NavigationPathSegment> folders, FrameworkElement chevron, string path)
    {
        _crumbFolderStoryboard?.Stop();
        _crumbFolderClosing = false;
        CrumbFolderItems.Children.Clear();
        Style? itemStyle = null;
        if (Application.Current.Resources.TryGetValue("FilesMate.ContextMenuItemStyle", out var resource)
            && resource is Style style)
        {
            itemStyle = style;
        }

        foreach (var folder in folders)
        {
            var item = new Button
            {
                Content = folder.Name,
                Tag = folder.Path,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = itemStyle,
            };
            AutomationProperties.SetName(item, folder.Name);
            ToolTipService.SetToolTip(item, folder.Path);
            item.Click += CrumbChild_Click;
            CrumbFolderItems.Children.Add(item);
        }

        _crumbChevron = chevron;
        _crumbFlyoutPath = path;
        CrumbFolderPopup.PlacementTarget = chevron;
        CrumbFolderPopup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
        CrumbFolderHost.Opacity = 0;
        CrumbFolderSlide.Y = CrumbFolderSlideOffset;
        CrumbFolderPopup.IsOpen = true;
        AnimateCrumbFolder(open: true);
    }

    private void RequestCrumbFolderClose()
    {
        if (_crumbFolderClosePending || _crumbFolderClosing || !CrumbFolderPopup.IsOpen)
        {
            return;
        }

        _crumbFolderClosePending = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _crumbFolderClosePending = false;
                if (IsLoaded && CrumbFolderPopup.IsOpen)
                {
                    AnimateCrumbFolder(open: false);
                }
            }))
        {
            _crumbFolderClosePending = false;
        }
    }

    private void AnimateCrumbFolder(bool open)
    {
        _crumbFolderStoryboard?.Stop();
        _crumbFolderStoryboard = null;
        _crumbFolderClosing = !open;
        var duration = App.Motion.Resolve(MotionDurations.Fast);
        var opacityTo = open ? 1d : 0d;
        var slideTo = open ? 0d : CrumbFolderSlideOffset;
        if (duration <= TimeSpan.Zero)
        {
            CrumbFolderHost.Opacity = opacityTo;
            CrumbFolderSlide.Y = slideTo;
            if (!open)
            {
                FinishCrumbFolderClose();
            }

            return;
        }

        var opacityEase = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        var slideEase = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        var opacity = new DoubleAnimation { To = opacityTo, Duration = duration, EasingFunction = opacityEase };
        var slide = new DoubleAnimation { To = slideTo, Duration = duration, EasingFunction = slideEase };
        Storyboard.SetTarget(opacity, CrumbFolderHost);
        Storyboard.SetTargetProperty(opacity, nameof(UIElement.Opacity));
        Storyboard.SetTarget(slide, CrumbFolderSlide);
        Storyboard.SetTargetProperty(slide, nameof(TranslateTransform.Y));
        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(slide);
        if (!open)
        {
            storyboard.Completed += (_, _) =>
            {
                if (_crumbFolderClosing)
                {
                    FinishCrumbFolderClose();
                }
            };
        }

        _crumbFolderStoryboard = storyboard;
        storyboard.Begin();
    }

    private void FinishCrumbFolderClose()
    {
        CrumbFolderPopup.IsOpen = false;
        CrumbFolderPopup.PlacementTarget = null;
        CrumbFolderItems.Children.Clear();
        CrumbFolderHost.Opacity = 0;
        CrumbFolderSlide.Y = CrumbFolderSlideOffset;
        _crumbFlyoutPath = null;
        _crumbChevron = null;
        _crumbFolderClosing = false;
        _crumbFolderStoryboard = null;
    }

    private void CrumbChild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && !string.IsNullOrEmpty(path))
        {
            AnimateCrumbFolder(open: false);
            RaiseCrumb(path);
        }
    }

    private void RaiseCrumb(string path)
    {
        _ = DispatcherQueue.TryEnqueue(() => CrumbClicked?.Invoke(this, path));
    }

    private static bool IsInside(DependencyObject source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void ShowErrorVisual(string? message)
    {
        if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var resource)
            && resource is Brush critical)
        {
            PathHost.BorderBrush = critical;
        }

        ToolTipService.SetToolTip(PathHost, message);
    }

    private void ClearPathErrorVisual()
    {
        if (_pathBorder is not null)
        {
            PathHost.BorderBrush = _pathBorder;
        }

        ToolTipService.SetToolTip(PathHost, _session.Path);
    }

    private void RebuildCrumbs(string path)
    {
        if (string.Equals(_crumbPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _crumbPath = path;
        _segments = [];
        if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device))
        {
            var root = device with { Segments = [] };
            _segments.Add(new NavigationPathSegment(root.Name, root.Uri, "\uE8EA"));
            for (var i = 0; i < device.Segments.Length; i++)
            {
                var child = device with { Segments = device.Segments[..(i + 1)] };
                _segments.Add(new NavigationPathSegment(child.Name, child.Uri));
            }
            PublishCrumbs();
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            PublishCrumbs();
            return;
        }

        if (HomeLocation.IsHome(path))
        {
            _segments.Add(new NavigationPathSegment(StringTable.Get("Home"), HomeLocation.Uri, LocationCaption.HomeGlyph));
            PublishCrumbs();
            return;
        }

        if (TagLocation.TryParse(path, out var tagId))
        {
            var name = ResolveTagName?.Invoke(tagId) ?? StringTable.Get("Nav_Tags");
            _segments.Add(new NavigationPathSegment(name, path, LocationCaption.TagGlyph));
            PublishCrumbs();
            return;
        }

        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        var unc = normalized.StartsWith("\\\\", StringComparison.Ordinal);
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            PublishCrumbs();
            return;
        }

        string acc;
        int start;
        if (unc && parts.Length >= 2)
        {
            acc = "\\\\" + parts[0] + "\\" + parts[1];
            _segments.Add(new NavigationPathSegment(acc, acc));
            start = 2;
        }
        else
        {
            acc = parts[0].EndsWith(':') ? parts[0] + "\\" : parts[0];
            _segments.Add(new NavigationPathSegment(
                acc,
                acc,
                CloudLocation.IsRoot(acc) ? CloudLocation.Glyph : null));
            start = 1;
        }

        for (var i = start; i < parts.Length; i++)
        {
            acc = acc.EndsWith('\\') ? acc + parts[i] : acc + "\\" + parts[i];
            _segments.Add(new NavigationPathSegment(
                parts[i],
                acc,
                CloudLocation.IsRoot(acc) ? CloudLocation.Glyph : null));
        }

        PublishCrumbs();
    }

    private void PublishCrumbs()
    {
        _crumbRequest?.Cancel();
        if (CrumbFolderPopup.IsOpen && !_crumbFolderClosing)
        {
            FinishCrumbFolderClose();
        }

        BuildAdaptiveCrumbs();
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ClearCrumbHover);
    }

    private void ClearCrumbHover()
    {
        ResetButtonStates(Crumbs);
    }

    private static void ResetButtonStates(DependencyObject node)
    {
        if (node is Button button)
        {
            VisualStateManager.GoToState(button, "Normal", useTransitions: false);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            ResetButtonStates(VisualTreeHelper.GetChild(node, i));
        }
    }
}
