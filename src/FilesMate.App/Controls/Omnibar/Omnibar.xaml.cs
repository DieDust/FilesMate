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
    private bool _editRequestPending;
    private bool _searchFocused;
    private bool _searchHovered;
    private bool _globalSearchScope;
    private string _localSearchDraft = string.Empty;
    private string _globalSearchDraft = string.Empty;
    private string _submittedLocalQuery = string.Empty;
    private bool _overflowPending;
    private CancellationTokenSource? _crumbRequest;
    private Storyboard? _crumbFolderStoryboard;
    private const double CrumbFolderSlideOffset = -8;
    private FrameworkElement? _crumbChevron;
    private string? _crumbFlyoutPath;
    private string? _crumbPath;
    private int _crumbFolderVersion;
    private bool _crumbFolderClosePending;

    public Omnibar()
    {
        InitializeComponent();
        InitializePathContextMenu();
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
            UpdateSearchPlaceholder();
            ApplyOverflow();
            ApplyMode(animate: false);
            ApplySearchLayout();
#if FILESMATE_UI_TEST
            if (Environment.GetEnvironmentVariable("FILESMATE_BREADCRUMB_SMOKE") == "1") _ = RunBreadcrumbSmokeAsync();
#endif
        };
        Unloaded += (_, _) =>
        {
            FinishCrumbFolderClose();
            SetCrumbDropTarget(null);
        };
    }

    public event RoutedEventHandler? BackClicked;

    public event RoutedEventHandler? ForwardClicked;

    public event RoutedEventHandler? UpClicked;

    public event RoutedEventHandler? RefreshClicked;

    public event EventHandler<string>? PathSubmitted;

    public event EventHandler<string>? CrumbClicked;

    public event EventHandler<string>? LocalQueryChanged;
    public string LocalQuery => _globalSearchScope ? string.Empty : _submittedLocalQuery;
    public event EventHandler<FilesMate.Search.SearchPageRequest>? MoreSearchRequested;

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
                _pathInputLifetime.Changed();
                _searchFocused = false;
                _localSearchDraft = string.Empty;
                _submittedLocalQuery = string.Empty;
                // A folder change cannot change the saved scope or copy its query.
                SetSearchText(_globalSearchScope ? _globalSearchDraft : string.Empty);
                FinishCrumbFolderClose();
            }

            _session.SetPath(value);
            UpdateSearchPlaceholder();
            if (_session.Mode != OmnibarMode.PathEdit)
            {
                PathBox.Text = DisplayPath;
                RebuildCrumbs(DisplayPath);
                ClearPathErrorVisual();
            }
        }
    }

    public void BeginPathEdit()
    {
        _pathInputLifetime.Changed();
        _pasteFailureRestore = null;
        _pathEditBase = DisplayDirectory;
        FinishCrumbFolderClose();
        _session.BeginPathEdit();
        _session.SetDraft(DisplayPath);
        PathBox.Text = _session.Draft;
        // Editing is a mode switch, not content navigation. Apply it atomically
        // so focus cannot land on the outgoing breadcrumb tree for one frame.
        ApplyMode(animate: false);
        PathBox.Focus(FocusState.Programmatic);
        PathBox.SelectAll();
    }

    public void BeginSearch()
    {
        FinishCrumbFolderClose();
        ApplySearchLayout();
        FocusSearchBox();
    }

    public async void BeginGlobalSearch()
    {
        try
        {
            var save = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { GlobalSearchScope = true });
            ApplySearchScope(true);
            await save;
            BeginSearch();
        }
        catch (Exception error)
        {
            App.AppendCrashRecord("SearchScope", error);
            ApplySearchScope(App.ExplorerPreferences.GlobalSearchScope);
        }
    }

    // Dismiss ends focus mode; the field and draft remain visible.
    public void DismissSearch()
    {
        _searchFocused = false;
        ApplySearchChrome();
        UpdatePlaceholderVisual();
    }

    public bool IsSearchOpen => _searchFocused;
    public bool IsSearchSource(DependencyObject source) => IsInside(source, SearchCluster);
    public bool IsCrumbFolderSource(DependencyObject source) =>
        (_crumbChevron is not null && IsInside(source, _crumbChevron))
        || IsInside(source, CrumbFolderHost);
    public void DismissCrumbFolders() => RequestCrumbFolderClose();
    private void SearchButton_Click(object sender, RoutedEventArgs e) => SubmitSearch();

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        if (!_globalSearchScope && _inlineFolderSearch)
        {
            _submittedLocalQuery = string.Empty;
            LocalQueryChanged?.Invoke(this, string.Empty);
        }
        BeginSearch();
    }

    private void SubmitSearch()
    {
        var target = FolderSearchPresentation.SubmitTarget(_globalSearchScope, _inlineFolderSearch);
        if (target == SearchSubmitTarget.Unavailable) { UpdateSearchPlaceholder(); return; }
        if (target == SearchSubmitTarget.Global)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) { BeginSearch(); return; }
            _globalSearchDraft = SearchBox.Text;
            MoreSearchRequested?.Invoke(this, new FilesMate.Search.SearchPageRequest(SearchBox.Text,
                Sort: FilesMate.Search.SearchSortConfiguration.Load(System.IO.Path.GetDirectoryName(
                    Program.SettingsPath(LanguageSettings.DefaultFilePath)))));
            return;
        }
        _localSearchDraft = _submittedLocalQuery = SearchBox.Text;
        _session.SetFilter(_submittedLocalQuery);
        LocalQueryChanged?.Invoke(this, _submittedLocalQuery);
    }

    private void ApplySearchLayout()
    {
        if (SearchSizer is null || SearchScopeButton is null) return;
        var stacked = ActualWidth < 720;
        Grid.SetRow(SearchCluster, stacked ? 1 : 0);
        Grid.SetColumn(SearchCluster, stacked ? 0 : 2);
        Grid.SetColumnSpan(SearchCluster, stacked ? 3 : 1);
        SearchCluster.Margin = new Thickness(0, 4, 0, 4);
        SearchSizer.Width = FolderSearchPresentation.ControlWidth(ActualWidth, stacked);
        UpdateSearchPlaceholder();
    }

    private void SetSearchText(string text)
    {
        var previous = _syncingInlineSearch;
        _syncingInlineSearch = true;
        try { SearchBox.Text = text; }
        finally { _syncingInlineSearch = previous; }
    }

    private void ApplySearchScope(bool global)
    {
        if (_globalSearchScope != global)
        {
            if (_globalSearchScope) _globalSearchDraft = SearchBox.Text;
            else _localSearchDraft = SearchBox.Text;
            _globalSearchScope = global;
            SetSearchText(global ? _globalSearchDraft : _localSearchDraft);
            // Keep a local draft for later submission, never a hidden active filter.
            if (_inlineFolderSearch && _submittedLocalQuery.Length > 0)
            {
                _submittedLocalQuery = string.Empty;
                LocalQueryChanged?.Invoke(this, string.Empty);
            }
        }
        UpdateSearchPlaceholder();
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.Select(SearchBox.Text.Length, 0);
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _searchFocused = true;
        FinishCrumbFolderClose();
        ApplySearchChrome();
        UpdatePlaceholderVisual();
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _searchFocused = false;
        ApplySearchChrome();
        UpdatePlaceholderVisual();
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
        Theming.ThemeResources.Bind(SearchHost, Control.BorderBrushProperty,
            _searchFocused ? "FilesMate.Selection.AccentBrush" : _searchHovered
                ? "FilesMate.Glass.BorderStrongBrush" : "FilesMate.AddressBar.BorderBrush");
    }

    private void UpdateSearchPlaceholder()
    {
        var key = _globalSearchScope ? "SearchPlaceholderHome"
            : !_inlineFolderSearch ? "Search_SelectFolderOrGlobal"
            : _inlineRecursiveSearch ? "Inline_RecursiveScope" : "Inline_CurrentScope";
        var text = StringTable.Get(key);
        SearchScopeIcon.Glyph = _globalSearchScope ? "\uE770" : "\uE8B7";
        AutomationProperties.SetName(SearchScopeButton,
            StringTable.Get(_globalSearchScope ? "GlobalSearch" : "Inline_CurrentScope"));
        ToolTipService.SetToolTip(SearchScopeButton, text);
        // The scope is already visible; do not repeat it inside the short query field.
        SearchPlaceholderLabel.Text = StringTable.Get("SearchPage_Search");
        AutomationProperties.SetName(SearchBox, text);
        ToolTipService.SetToolTip(SearchBox, text);
        var submit = StringTable.Get(_globalSearchScope ? "Search_SubmitGlobal" : "Search_SubmitFolder");
        AutomationProperties.SetName(SearchButton, submit);
        ToolTipService.SetToolTip(SearchButton, _inlineFolderSearch || _globalSearchScope ? submit : text);
        SearchButton.IsEnabled = _inlineFolderSearch || _globalSearchScope;
        UpdatePlaceholderVisual();
    }

    private void UpdatePlaceholderVisual()
    {
        SearchPlaceholderLabel.Opacity = !_searchFocused && string.IsNullOrEmpty(SearchBox.Text) ? 1 : 0;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public bool CancelMode()
    {
        // Escape also cancels a display-mode PasteGo still reading clipboard.
        CancelPendingPathPaste();
        if (!_session.Cancel())
        {
            return false;
        }

        _pathInputLifetime.Changed();
        _pasteFailureRestore = null;
        ApplySelectionDisplay();
        ClearPathErrorVisual();
        ApplyMode(animate: false);
        ModeCanceled?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ApplyCommittedPath(string path)
    {
        _pathInputLifetime.Changed();
        _pasteFailureRestore = null;
        _ = _session.Cancel();
        Text = path;
        ApplyMode(animate: false);
    }

    public bool TryCommitPath(Func<string, string> commit)
    {
        _session.SetDraft(PathBox.Text);
        if (!_session.SubmitPath(commit))
        {
            if (_pasteFailureRestore is { } restore && restore.Revision == AddressInputRevision)
            {
                PathBox.Text = restore.Draft;
                _session.SetDraft(restore.Draft);
                _pathEditBase = restore.Base;
            }
            _pasteFailureRestore = null;
            ShowErrorVisual(_session.PathError);
            ApplyMode(animate: false);
            PathBox.Focus(FocusState.Programmatic);
            return false;
        }

        _pathInputLifetime.Changed();
        _pasteFailureRestore = null;
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
        ApplySearchLayout();
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
            _session.Mode == OmnibarMode.PathEdit ? _session.PathError : DisplayPath);
    }

    private void PathEditSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(PathEditSurface).Properties.IsLeftButtonPressed) return;
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
        if (!e.GetCurrentPoint(PathHost).Properties.IsLeftButtonPressed) return;
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
            CancelPendingPathPaste();
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
        if (_session.Mode != OmnibarMode.PathEdit || XamlRoot is null || _pathContextOpen)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (focused is null || !IsInside(focused, PathBox))
        {
            CancelMode();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncingInlineSearch)
        {
            if (_globalSearchScope) _globalSearchDraft = SearchBox.Text;
            else _localSearchDraft = SearchBox.Text;
        }
        // Draft editing never filters a pane or runs a global query.
        UpdatePlaceholderVisual();
    }

    private void SearchScopeButton_Click(object sender, RoutedEventArgs e)
    {
        var items = new StackPanel { Spacing = 0, MinWidth = 148 };
        var flyout = new Flyout
        {
            Content = items, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            AreOpenCloseAnimationsEnabled = false,
            FlyoutPresenterStyle = (Style)Application.Current.Resources["FilesMate.RoundedFlyoutPresenterStyle"]
        };
        Theming.FlyoutTheme.FollowHost(flyout);
        foreach (var global in new[] { false, true })
        {
            var text = StringTable.Get(global ? "GlobalSearch" : "Inline_CurrentScope");
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new FontIcon
            {
                Glyph = global ? "\uE770" : "\uE8B7", FontFamily = new("Segoe Fluent Icons"),
                FontSize = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            });
            var label = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); content.Children.Add(label);
            var button = new Button
            {
                Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = global == _globalSearchScope ? (Style)Resources["SearchScopeSelectedItemStyle"]
                    : (Style)Application.Current.Resources["FilesMate.ContextMenuItemStyle"]
            };
            AutomationProperties.SetAutomationId(button, global ? "SearchScopeGlobal" : "SearchScopeFolder");
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => { flyout.Hide(); ChangeSearchScope(global); };
            items.Children.Add(button);
        }
        FlyoutBase.SetAttachedFlyout(SearchScopeButton, flyout);
        flyout.ShowAt(SearchScopeButton);
    }

    private async void ChangeSearchScope(bool global)
    {
        if (global == App.ExplorerPreferences.GlobalSearchScope) { ApplySearchScope(global); return; }
        try
        {
            // Change shared state before nested pane sync. Receivers do not save.
            var save = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { GlobalSearchScope = global });
            ApplySearchScope(global);
            await save;
        }
        catch (Exception error)
        {
            App.AppendCrashRecord("SearchScope", error);
            ApplySearchScope(App.ExplorerPreferences.GlobalSearchScope);
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { SubmitSearch(); e.Handled = true; }
        else if (e.Key == VirtualKey.Escape)
        {
            DismissSearch();
            ModeCanceled?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void CrumbName_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && !string.IsNullOrEmpty(path))
        {
            DismissSearch();
            FinishCrumbFolderClose();
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
        if (_crumbFolderClosePending)
        {
            FinishCrumbFolderClose();
        }

        _crumbRequest?.Cancel();
        var version = ++_crumbFolderVersion;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (version == _crumbFolderVersion)
                {
                    ShowCrumbFolders(path, chevron);
                }
            }))
        {
            FinishCrumbFolderClose();
        }
    }

    private async void ShowCrumbFolders(string path, FrameworkElement chevron)
    {
        if (!IsLoaded || !chevron.IsLoaded || chevron.XamlRoot is null)
        {
            FinishCrumbFolderClose();
            return;
        }

        if (!_crumbFolderClosePending && CrumbFolderPopup.IsOpen
            && string.Equals(_crumbFlyoutPath, path, StringComparison.OrdinalIgnoreCase))
        {
            AnimateCrumbFolder(open: false);
            return;
        }

        // The previous menu must not remain actionable while a different folder loads.
        FinishCrumbFolderClose();
        var version = _crumbFolderVersion;
        var request = new CancellationTokenSource();
        _crumbRequest = request;
        IReadOnlyList<NavigationPathSegment> folders;
        try
        {
            folders = await PathChildren.FoldersAsync(path, App.ExplorerPreferences.ShowHiddenFiles, request.Token);
            if (request.IsCancellationRequested || version != _crumbFolderVersion
                || !IsLoaded || !chevron.IsLoaded || chevron.XamlRoot is null)
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
        // A replacement owns a new lifetime; any queued close/open belongs to the old one.
        FinishCrumbFolderClose();
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
            item.BorderThickness = new Thickness(1);
            ConfigureCrumbDrop(item, folder.Path);
            CrumbFolderItems.Children.Add(item);
        }

        _crumbChevron = chevron;
        _crumbFlyoutPath = path;
        CrumbFolderPopup.PlacementTarget = chevron;
        CrumbFolderPopup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
        CrumbFolderHost.Opacity = 0;
        CrumbFolderSlide.Y = CrumbFolderSlideOffset;
        CrumbFolderHost.IsHitTestVisible = true;
        CrumbFolderPopup.IsOpen = true;
        AnimateCrumbFolder(open: true);
    }

    private void RequestCrumbFolderClose()
    {
        _crumbRequest?.Cancel();
        if (_crumbFolderClosePending)
        {
            return;
        }

        if (!IsLoaded || !CrumbFolderPopup.IsOpen)
        {
            FinishCrumbFolderClose();
            return;
        }

        // Outside presses finish their input route before we detach the popup tree.
        // Hide and disarm it now; there is no exit animation or animation-owned close.
        _crumbFolderClosePending = true;
        var version = ++_crumbFolderVersion;
        _crumbFolderStoryboard?.Stop();
        _crumbFolderStoryboard = null;
        CrumbFolderHost.IsHitTestVisible = false;
        CrumbFolderHost.Opacity = 0;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (version == _crumbFolderVersion && _crumbFolderClosePending)
                {
                    FinishCrumbFolderClose();
                }
            }))
        {
            FinishCrumbFolderClose();
        }
    }

    private void AnimateCrumbFolder(bool open)
    {
        if (!open)
        {
            FinishCrumbFolderClose();
            return;
        }

        _crumbFolderStoryboard?.Stop();
        _crumbFolderStoryboard = null;
        var duration = App.Motion.Resolve(MotionDurations.Fast);
        if (duration <= TimeSpan.Zero)
        {
            CrumbFolderHost.Opacity = 1;
            CrumbFolderSlide.Y = 0;
            return;
        }

        var opacity = new DoubleAnimation
        {
            To = 1, Duration = duration, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var slide = new DoubleAnimation
        {
            To = 0, Duration = duration, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, CrumbFolderHost);
        Storyboard.SetTargetProperty(opacity, nameof(UIElement.Opacity));
        Storyboard.SetTarget(slide, CrumbFolderSlide);
        Storyboard.SetTargetProperty(slide, nameof(TranslateTransform.Y));
        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(slide);
        _crumbFolderStoryboard = storyboard;
        storyboard.Begin();
    }

    private void FinishCrumbFolderClose()
    {
        ++_crumbFolderVersion;
        _crumbFolderClosePending = false;
        _crumbRequest?.Cancel();
        _crumbFolderStoryboard?.Stop();
        _crumbFolderStoryboard = null;
        CrumbFolderHost.IsHitTestVisible = false;
        CrumbFolderPopup.IsOpen = false;
        CrumbFolderPopup.PlacementTarget = null;
        CrumbFolderItems.Children.Clear();
        CrumbFolderHost.Opacity = 0;
        CrumbFolderSlide.Y = CrumbFolderSlideOffset;
        _crumbFlyoutPath = null;
        _crumbChevron = null;
    }

    private void CrumbFolder_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || !CrumbFolderPopup.IsOpen || _crumbFolderClosePending
            || e.OriginalSource is not DependencyObject source || !IsCrumbFolderSource(source))
        {
            return;
        }

        var chevron = _crumbChevron;
        FinishCrumbFolderClose();
        e.Handled = true;
        // Only Escape restores the opener. Outside presses, navigation and unload
        // must leave focus with the control the user is moving to.
        if (IsLoaded && chevron is Control { IsLoaded: true, IsEnabled: true } opener
            && chevron.XamlRoot == XamlRoot)
        {
            opener.Focus(FocusState.Keyboard);
        }
    }

    private void CrumbChild_Click(object sender, RoutedEventArgs e)
    {
        if (_crumbFolderClosePending || !CrumbFolderPopup.IsOpen || !CrumbFolderHost.IsHitTestVisible)
        {
            return;
        }

        if (sender is FrameworkElement { Tag: string path } item && !string.IsNullOrEmpty(path)
            && CrumbFolderItems.Children.Contains(item))
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
        Theming.ThemeResources.Bind(PathHost, Control.BorderBrushProperty, "SystemFillColorCriticalBrush");

        ToolTipService.SetToolTip(PathHost, message);
    }

    private void ClearPathErrorVisual()
    {
        Theming.ThemeResources.Bind(PathHost, Control.BorderBrushProperty, "FilesMate.AddressBar.BorderBrush");

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
        FinishCrumbFolderClose();
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
