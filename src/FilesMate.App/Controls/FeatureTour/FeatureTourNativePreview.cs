using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Shortcuts;
using FilesMate.App.Theming;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FeatureTour;

/// <summary>Production file and preview components, with memory-only command adapters.</summary>
internal sealed class FeatureTourNativePreview : StackPanel, IDisposable
{
    private readonly string _topic;
    private readonly FeatureTourChoices _choices;
    private readonly FeatureTourPracticeState _state;
    private readonly bool _practice;
    private readonly StackPanel _scene = new() { Spacing = 7 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly FileDetailsSurface _surface = new() { Height = 170, HorizontalAlignment = HorizontalAlignment.Stretch };
    private QuickPreviewWindow? _preview;
    private QuickPreviewDismissal? _previewInput;
    private readonly FeatureTourMemoryData _memory;
    private readonly LocalSkinScope _skin;
    private readonly Button? _pause;
    private readonly DispatcherTimer? _timer;
    private RecursiveProjection? _projection;
    private int _phase;
    private bool _disposed, _running, _binding, _ready;
    public event EventHandler? ProgressChanged;

    public FeatureTourNativePreview(string topic, FeatureTourChoices choices, FeatureTourPracticeState state, bool practice)
    {
        _topic = topic; _choices = choices; _state = state; _practice = practice; Spacing = 7;
        _skin = new(_scene);
        ApplyPreviewTheme();
        _surface.Height = topic is "opening" or "appearance" ? 104 : 216;
        _memory = new(topic == "opening");
        _surface.UseMemoryPresentation(folder => folder ? _choices.FolderOpening : _choices.FileOpening);
        AutomationProperties.SetAutomationId(_surface, "TourNativeFileSurface");
        AutomationProperties.SetName(_surface, "Project");
        _surface.SelectionChanged += SelectionChanged;
        _surface.OpenRequested += OpenRequested;
        _surface.QuickPreviewRequested += (_, _) => TogglePreview();
        _surface.RecursiveToggleRequested += (_, id) => SetExpanded(id, !(_projection?.IsExpanded(id) ?? false));
        _surface.RecursiveExpansionRequested += (_, request) => SetExpanded(request.EntryId, request.Expanded, request.Subtree);
        _surface.RecursiveDepthRequested += (_, request) =>
        {
            if (!_ready || _disposed) return;
            Stop();
            var project = _memory.Id("Project"); var images = _memory.Id("Images");
            foreach (var id in request.EntryIds)
            {
                if (id == 0) { _state.RootExpanded = request.Depth > 1; _state.ChildExpanded = request.Depth > 2; }
                else if (id == project) { _state.RootExpanded = request.Depth > 0; _state.ChildExpanded = request.Depth > 1; }
                else if (id == images) _state.ChildExpanded = request.Depth > 0;
            }
            BindSurface(); Changed();
        };
        _surface.RecursiveDesiredExpanded = id => _projection?.IsExpanded(id) ?? false;
        _surface.RecursiveDesiredSubtreeExpanded = id => _projection?.IsExpanded(id) ?? false;
        Children.Add(_scene);
        AutomationProperties.SetAutomationId(_status, "TourPracticeStatus");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (topic != "appearance") actions.Children.Add(ActionButton(StringTable.Get("Tour_Reset"), "TourPracticeReset", () => { _state.Reset(); Render(); Changed(); }));
        if (!practice)
        {
            _pause = new Button { Content = StringTable.Get("Tour_Pause") };
            var replay = new Button { Content = StringTable.Get("Tour_Replay") };
            AutomationProperties.SetAutomationId(_pause, "TourDemoPause"); AutomationProperties.SetAutomationId(replay, "TourDemoReplay");
            _pause.Click += (_, _) => { if (_running) Stop(); else Start(false); };
            replay.Click += (_, _) => Start(true);
            actions.Children.Add(_pause); actions.Children.Add(replay);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) }; _timer.Tick += Tick;
        }
        Children.Add(actions); Loaded += OnLoaded; Unloaded += OnUnloaded; PreviewKeyDown += ScopedPreviewKey;
        Status(); _ = LoadMemoryAsync();
    }
    private async Task LoadMemoryAsync()
    {
        try
        {
            await _memory.LoadAsync();
            if (_disposed) return;
            if (_memory.Snapshot.State != RecursiveSessionState.Completed) throw new InvalidOperationException("Memory guide data did not complete.");
            _ready = true; Render();
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (ObjectDisposedException) when (_disposed) { }
    }
    private static bool MotionAllowed => App.Motion?.Standard > TimeSpan.Zero;
    private void OnLoaded(object sender, RoutedEventArgs args) { if (!_practice && !_disposed && MotionAllowed) Start(true); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => Stop();
    private void Stop()
    {
        _timer?.Stop(); _running = false;
        if (_pause is not null) { _pause.Content = StringTable.Get("Tour_Resume"); _pause.Visibility = MotionAllowed ? Visibility.Visible : Visibility.Collapsed; }
    }
    private void Start(bool restart)
    {
        if (_disposed || _practice || !MotionAllowed) { Stop(); return; }
        Stop(); if (restart || _phase >= 4) { _phase = 0; _state.Reset(); }
        _running = true; Render(); _pause!.Content = StringTable.Get("Tour_Pause"); _timer!.Start();
    }
    private void Tick(object? sender, object args)
    {
        if (!MotionAllowed) { Stop(); return; }
        _phase++; _state.RootExpanded = _phase >= 1; _state.ChildExpanded = _phase >= 2;
        _state.ImageFilter = _phase >= 3; _state.Selected = _phase >= 4 ? "cover.jpg" : "";
        BindSurface(); Status(); if (_phase >= 4) Stop();
    }
    private Button ActionButton(string text, string id, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 4, 8, 4), MinWidth = 0 };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => { Stop(); action(); };
        return button;
    }
    private void Changed()
    {
        _state.Completed |= _topic switch
        {
            "opening" => _state.FolderOpened && _state.PreviewOpened,
            "recursive" => _state.RootExpanded && _state.ChildExpanded && _state.Selected == "cover.jpg",
            "filters" => _state.FilterUsed && _state.FilterCleared,
            "search" => _state.Query.Contains("cover", StringComparison.OrdinalIgnoreCase) && _state.Selected == "cover.jpg",
            "browsing" => _state.ViewChanged && _state.TabChanged,
            "appearance" => _state.ThemeChanged && _state.AccentChanged,
            _ => false,
        };
        Status(); ProgressChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Status()
    {
        if (_topic == "appearance" && _practice)
        {
            _status.Text = StringTable.Get("Tour_AppearanceChoose");
            AutomationProperties.SetName(_status, _status.Text);
            return;
        }
        _status.Text = _topic == "opening"
            ? StringTable.Get(!_state.FolderOpened ? "Tour_TryExpand" : _state.Selected != "notes.txt" && !_state.PreviewOpened ? "Tour_TrySelect" : !_state.PreviewOpened ? "Tour_TryPreview" : _state.PreviewVisible ? "Tour_PreviewDone" : "Tour_PracticeDone")
            : StringTable.Get(_state.Completed ? "Tour_PracticeDone" : "Tour_PracticeTry") + " · " + StringTable.Get("Tour_Task_" + _topic);
        if (_topic == "opening" && _state.FolderOpened && _state.Selected == "notes.txt" && !_state.PreviewOpened)
            _status.Text = StringTable.Format("Tour_TryPreview", PreviewKeys);
        AutomationProperties.SetName(_status, _status.Text);
    }
    private void SelectionChanged(object? sender, EventArgs args)
    {
        if (_binding || !_ready) return;
        Stop(); var id = _surface.Selection.PrimaryId;
        _state.Selected = id is > 0 ? _memory.Snapshot.GetEntry(id.Value).Name : "";
        if (_preview is { } preview)
        {
            if (id is > 0 && _memory.Snapshot.GetEntry(id.Value) is { Kind: EntryKind.File } entry)
                preview.BindMemoryText(entry, StringTable.Get("Tour_SampleNotes"));
            else preview.Dismiss();
        }
        Changed();
    }
    private void OpenRequested(object? sender, FileEntryCore entry)
    {
        Stop(); _state.Selected = entry.Name;
        if (entry.Name == "notes.txt") _state.FileOpened = true;
        if (entry.Kind == EntryKind.File) ShowPreview(entry);
        Changed();
    }
    private void SetExpanded(int id, bool expanded, bool subtree = false)
    {
        if (!_ready) return;
        Stop(); var name = _memory.Snapshot.GetEntry(id).Name;
        if (name == "Project") { _state.RootExpanded = expanded; if (expanded) _state.FolderOpened = true; }
        else if (name == "Images") _state.ChildExpanded = expanded;
        if (subtree && name == "Project") _state.ChildExpanded = expanded;
        BindSurface(); Changed();
    }
    private void BindSurface()
    {
        if (!_ready || _disposed) return;
        _binding = true;
        try
        {
            var expansion = new RecursiveExpansionState();
            var project = _memory.Id("Project"); var images = _memory.Id("Images");
            if (project > 0) expansion.SetExpandedWithUndo(project, _state.RootExpanded);
            if (images > 0) expansion.SetExpandedWithUndo(images, _state.ChildExpanded);
            var filter = new EntryFilter { Query = _topic == "search" ? _state.Query : "" };
            Func<FileEntryCore, bool>? match = _topic == "filters" ? entry =>
                (!_state.ImageFilter || entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                && (!_state.DateFilter || entry.ModifiedUtcTicks >= DateTime.UtcNow.Date.Ticks)
                && (!_state.SizeFilter || entry.Size > 1_048_576) : null;
            _projection = _memory.Snapshot.BuildProjection(EntrySort.Name, filter, StringComparer.OrdinalIgnoreCase,
                additionalMatch: match, expansionPlan: expansion.Capture());
            var tree = _topic is "opening" or "recursive" or "filters";
            _surface.SetRecursiveMode(tree);
            _surface.SetColumns(DetailsColumn.Defaults().Select(c => c with { Visible = c.Id == DetailsColumnId.Name, Width = c.Id == DetailsColumnId.Name ? 340 : c.Width }).ToArray());
            var index = _projection.Index;
            if (!tree)
            {
                var wanted = _topic == "search" ? new EntryFilter { Query = _state.Query.Length == 0 ? "__no_sample_match__" : _state.Query, FilesOnly = true }
                    : new EntryFilter { Query = _state.Tab == "Images" ? "cover" : "notes", FilesOnly = true };
                index = EntryViewIndex.Build(_projection.Store.CaptureSnapshot(), EntrySort.Name, wanted, StringComparer.OrdinalIgnoreCase, 1);
            }
            var binding = new EntryViewBinding(_projection.Store, index, 1, _memory.Root, static _ => null!, tree ? _projection : null, static () => false);
            _surface.Bind(_projection.Store, index, 1, binding: binding);
            if (!tree) _surface.SetLayout(_state.View switch { FolderViewKind.Icons => FileLayoutKind.Grid, FolderViewKind.List => FileLayoutKind.List, _ => FileLayoutKind.Details });
            if (_state.Selected.Length > 0) _surface.SelectMemoryEntry(_state.Selected);
            else _surface.ClearSelection();
        }
        finally { _binding = false; }
    }
    private void Render()
    {
        if (!_ready || _disposed) return;
        _scene.Children.Clear();
        switch (_topic)
        {
            case "search":
                var query = new TextBox { PlaceholderText = StringTable.Get("Tour_SearchPlaceholder"), Text = _state.Query };
                AutomationProperties.SetAutomationId(query, "TourSearchQuery");
                query.TextChanged += (_, _) => { Stop(); _state.Query = query.Text; _state.Selected = ""; BindSurface(); Changed(); };
                _scene.Children.Add(query); break;
            case "filters": Filters(); break;
            case "browsing":
                var views = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetAutomationId(views, "TourPreviewView");
                foreach (var kind in Enum.GetValues<FolderViewKind>()) views.Items.Add(new ComboBoxItem { Content = StringTable.Get(kind switch { FolderViewKind.Icons => "Layout_LargeIcons", FolderViewKind.List => "Layout_List", _ => "Layout_Details" }), Tag = kind });
                views.SelectedIndex = (int)_state.View;
                views.SelectionChanged += (_, _) => { if (views.SelectedItem is ComboBoxItem { Tag: FolderViewKind kind }) { Stop(); _state.View = kind; _state.ViewChanged = kind != FolderViewKind.Details; BindSurface(); Changed(); } };
                _scene.Children.Add(views);
                var tabs = new TabView { IsAddTabButtonVisible = false, Height = 44 };
                AutomationProperties.SetAutomationId(tabs, "TourNativeTabs");
                foreach (var name in new[] { "Project", "Images" })
                {
                    var tab = new TabViewItem { Header = name, IsClosable = false, Tag = name };
                    AutomationProperties.SetAutomationId(tab, "TourTab-" + name); tabs.TabItems.Add(tab);
                }
                tabs.SelectedIndex = _state.Tab == "Images" ? 1 : 0;
                tabs.SelectionChanged += (_, _) => { if (tabs.SelectedItem is TabViewItem { Tag: string name }) { Stop(); _state.Tab = name; _state.TabChanged = name == "Images"; _state.Selected = ""; BindSurface(); Changed(); } };
                _scene.Children.Add(tabs); break;
            case "appearance":
                if (!_practice)
                {
                    var themes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    themes.Children.Add(ActionButton(StringTable.Get("ThemeLight"), "TourPreviewLight", () => { _state.Theme = ElementTheme.Light; _state.ThemeChanged = true; ApplyPreviewTheme(); Changed(); }));
                    themes.Children.Add(ActionButton(StringTable.Get("ThemeDark"), "TourPreviewDark", () => { _state.Theme = ElementTheme.Dark; _state.ThemeChanged = true; ApplyPreviewTheme(); Changed(); }));
                    themes.Children.Add(ActionButton(StringTable.Get("Accent_Green"), "TourPreviewGreen", () => { _state.GreenAccent = true; _state.AccentChanged = true; ApplyPreviewTheme(); Changed(); }));
                    _scene.Children.Add(themes);
                }
                _state.Selected = "notes.txt"; break;
        }
        _scene.Children.Add(_surface);
        if (_topic == "opening") _scene.Children.Add(ActionButton(StringTable.Format("Tour_PreviewButton", PreviewKeys), "TourPreviewFile", () => { _surface.FocusMemoryEntry("notes.txt"); TogglePreview(); }));
        ApplyPreviewTheme(); BindSurface(); Status();
    }
    private void ApplyPreviewTheme()
    {
        var appearance = PreviewAppearance;
        _skin.Apply(appearance);
        // Paint the memory host from the same semantic product palette. A
        // ThemeResource captured before this local scope joins the dialog can
        // retain the dialog's Dark brush after the local theme becomes Light.
        var dark = appearance.Theme == AppThemeKind.Dark || appearance.Theme == AppThemeKind.System
            && ContentDialogTheme.Resolve(_scene) == ElementTheme.Dark;
        _scene.Background = new SolidColorBrush(Themes.ShellStyleResources.FromArgb(SkinPalette.For(dark, appearance).Content));
    }
    private AppearanceSettings PreviewAppearance => AppearanceSettings.Default with
        {
            Theme = _practice ? _choices.Theme : _state.Theme switch { ElementTheme.Light => AppThemeKind.Light, ElementTheme.Dark => AppThemeKind.Dark, _ => _choices.Theme },
            Accent = _state.GreenAccent ? AccentKind.Green : _choices.Accent,
            CustomAccent = _choices.CustomAccent,
            TransparencyPercent = 0,
        };
    private void Filters()
    {
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        void Add(string text, string id, bool current, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = current }; AutomationProperties.SetAutomationId(box, id);
            box.Click += (_, _) => { Stop(); set(box.IsChecked == true); _state.FilterUsed = true; _state.RootExpanded = _state.ChildExpanded = true; BindSurface(); Changed(); }; filters.Children.Add(box);
        }
        Add(".jpg", "TourFilterType", _state.ImageFilter, value => _state.ImageFilter = value);
        Add(StringTable.Get("SearchPage_Today"), "TourFilterDate", _state.DateFilter, value => _state.DateFilter = value);
        Add("> 1 MB", "TourFilterSize", _state.SizeFilter, value => _state.SizeFilter = value);
        _scene.Children.Add(filters);
        _scene.Children.Add(ActionButton(StringTable.Get("Tour_ClearFilters"), "TourFilterClear", () => { _state.ImageFilter = _state.DateFilter = _state.SizeFilter = false; _state.FilterCleared = _state.FilterUsed; BindSurface(); Changed(); }));
    }
    private void TogglePreview()
    {
        if (!_ready || _surface.Selection.PrimaryId is not int id) return;
        if (_preview is not null) { ClosePreview(); return; }
        var entry = _memory.Snapshot.GetEntry(id); if (entry.Kind == EntryKind.File) ShowPreview(entry);
    }
    private void ShowPreview(FileEntryCore entry)
    {
        if (App.WindowForElement(this) is not { } owner) return;
        Stop();
        if (_preview is { } current) { current.BindMemoryText(entry, StringTable.Get("Tour_SampleNotes")); return; }
        var window = _preview = QuickPreviewWindow.ForMemory(owner, PreviewAppearance);
        window.BindMemoryText(entry, StringTable.Get("Tour_SampleNotes"));
        window.NavigateFile += (_, step) => _surface.MovePreviewSelection(step);
        _previewInput = new(XamlRoot.Content, window.Dismiss);
        window.Closed += (_, _) =>
        {
            if (_preview != window) return;
            _preview = null; _previewInput?.Dispose(); _previewInput = null;
            _state.PreviewVisible = false;
            if (_disposed) return;
            if (window.RestoreOwnerFocus && IsLoaded) _surface.FocusMemorySelection();
            Changed();
        };
        window.Activate();
        _state.PreviewOpened = _state.PreviewVisible = true;
        Changed();
    }
    private void ClosePreview()
    {
        _preview?.Close();
    }
    private void ScopedPreviewKey(object sender, KeyRoutedEventArgs args)
    {
        var inside = false;
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or RichEditBox or PasswordBox) return;
            inside |= ReferenceEquals(node, _surface);
        }
        if (!inside) return;
        if (args.Key == Windows.System.VirtualKey.Escape && _state.PreviewVisible) { args.Handled = true; ClosePreview(); }
        else if (new ShortcutGesture((ShortcutKey)args.Key, CurrentModifiers) == App.Shortcuts[ShortcutAction.Preview]
            || args.Key == Windows.System.VirtualKey.Space && CurrentModifiers == ShortcutModifiers.None && _state.PreviewVisible)
        { args.Handled = true; TogglePreview(); }
    }
    private static ShortcutModifiers CurrentModifiers
    {
        get
        {
            bool Down(Windows.System.VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var m = ShortcutModifiers.None;
            if (Down(Windows.System.VirtualKey.Control)) m |= ShortcutModifiers.Control;
            if (Down(Windows.System.VirtualKey.Menu)) m |= ShortcutModifiers.Menu;
            if (Down(Windows.System.VirtualKey.Shift)) m |= ShortcutModifiers.Shift;
            if (Down(Windows.System.VirtualKey.LeftWindows) || Down(Windows.System.VirtualKey.RightWindows)) m |= ShortcutModifiers.Windows;
            return m;
        }
    }
    private static string PreviewKeys => App.Shortcuts[ShortcutAction.Preview] is var gesture
        && gesture != new ShortcutGesture(ShortcutKey.P, ShortcutModifiers.Menu)
        && gesture != new ShortcutGesture(ShortcutKey.Space, ShortcutModifiers.None) ? "Space / " + gesture.DisplayText : "Space";
    public void RefreshChoices() { if (!_disposed) { ApplyPreviewTheme(); _surface.RefreshMemoryOpeningMode(); Status(); } }
    public bool TryClosePreview()
    {
        if (!_state.PreviewVisible) return false;
        var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (node is not null && !ReferenceEquals(node, _scene)) node = VisualTreeHelper.GetParent(node);
        if (node is null) return false;
        ClosePreview(); return true;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Stop(); if (_timer is not null) _timer.Tick -= Tick;
        Loaded -= OnLoaded; Unloaded -= OnUnloaded; PreviewKeyDown -= ScopedPreviewKey;
        _surface.SelectionChanged -= SelectionChanged; _surface.OpenRequested -= OpenRequested;
        _previewInput?.Dispose(); _preview?.Dismiss();
        _surface.ReleaseResources(); _memory.Dispose(); ProgressChanged = null; _scene.Children.Clear();
    }
}
