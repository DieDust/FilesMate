using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FeatureTour;

/// <summary>One native content host shared by first-run, release and manual tours.</summary>
internal sealed class FeatureTourSurface : Grid, IDisposable
{
    private readonly ContentControl _host = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _progress = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly string[] _pages;
    private FeatureTourNativePreview? _preview;
    private ComboBox? _hotkey;
    private bool _disposed;
    private readonly bool _practice;
    private readonly Dictionary<string, FeatureTourPracticeState> _practiceStates = new(StringComparer.Ordinal);

    public FeatureTourChoices Choices { get; }
    public string Page => _pages[PageIndex];
    public int PageIndex { get; private set; }
    public bool IsLast => PageIndex == _pages.Length - 1;
    public bool IsFirst => PageIndex == 0;
    public bool PageReady => !_practice || Page == "appearance" || _practiceStates[Page].Completed;
    public event EventHandler? PageChanged;
    public event EventHandler? PracticeChanged;
    public event Action<string>? SettingsRequested;

    public FeatureTourSurface(FeatureTourChoices choices, bool release, string? resumePage)
    {
        Choices = choices;
        _practice = !release;
        _pages = release ? ["recursive", "filters"] : ["appearance", "opening", "recursive", "search", "browsing"];
        foreach (var page in _pages) _practiceStates.Add(page, new());
        AutomationProperties.SetAutomationId(this, "FeatureTourRoot");
        AutomationProperties.SetAutomationId(_progress, "TourProgress");
        AutomationProperties.SetLiveSetting(_progress, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _progress.Margin = new Thickness(0, 0, 0, 10); Children.Add(_progress);
        _scroller = new ScrollViewer
        {
            Content = _host, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(_scroller, "TourPageScroller");
        SetRow(_scroller, 1); Children.Add(_scroller);
        var initial = Array.IndexOf(_pages, resumePage);
        PageIndex = Math.Max(0, initial);
        ShowPage();
    }

    public void Resize(double viewportWidth, double viewportHeight, double errorHeight)
    {
        Width = Math.Clamp(viewportWidth - 96, 180, 440);
        // Keep the native navigation footer still as page content changes.
        // A short window scrolls only the active page above that footer.
        _scroller.Height = _scroller.MaxHeight = Math.Clamp(viewportHeight - 250 - errorHeight, 80, 430);
    }

    public void Move(int direction)
    {
        var next = Math.Clamp(PageIndex + direction, 0, _pages.Length - 1);
        if (next == PageIndex) return;
        PageIndex = next; ShowPage(); PageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void GoTo(string page)
    {
        var index = Array.IndexOf(_pages, page);
        if (index < 0 || index == PageIndex) return;
        PageIndex = index; ShowPage(); PageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowPage()
    {
        CaptureEditors(); _hotkey = null;
        _preview?.Dispose(); _preview = null; _host.Content = null;
        var body = new StackPanel { Spacing = 10, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(body, "TourPage-" + Page);
        var key = Page switch { "opening" => "Opening", "recursive" => "Recursive", "filters" => "Filters", "search" => "Search", "browsing" => "Browsing", _ => "Appearance" };
        body.Children.Add(new TextBlock { Text = StringTable.Get("Tour_" + key + "Title"), FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!_practice) body.Children.Add(new TextBlock { Text = StringTable.Get("Tour_" + key + "Lead"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
        _preview = new FeatureTourNativePreview(Page, Choices, _practiceStates[Page], _practice);
        _preview.ProgressChanged += (_, _) => PracticeChanged?.Invoke(this, EventArgs.Empty);
        body.Children.Add(_preview);
        switch (Page)
        {
            case "opening":
                Opening(body, "Opening_FileTitle", "SetupFileOpening", Choices.FileOpening, value => { Choices.FileOpening = value; _preview?.RefreshChoices(); });
                Opening(body, "Opening_FolderTitle", "SetupFolderOpening", Choices.FolderOpening, value => { Choices.FolderOpening = value; _preview?.RefreshChoices(); });
                break;
            case "search":
                Toggle(body, "Search_Residency", "SetupSearchEnabled", Choices.Search, value => Choices.Search = value);
                var hotkey = new ComboBox { Header = StringTable.Get("SearchShortcut"), IsEditable = true, Text = Choices.Hotkey, HorizontalAlignment = HorizontalAlignment.Stretch };
                _hotkey = hotkey;
                foreach (var value in new[] { Choices.Hotkey, "Alt+Space", "Ctrl+Alt+Space", "Ctrl+Shift+Space" }.Distinct()) hotkey.Items.Add(value);
                hotkey.SelectedIndex = 0; hotkey.Text = Choices.Hotkey;
                hotkey.LostFocus += (_, _) => Choices.Hotkey = hotkey.Text;
                AutomationProperties.SetAutomationId(hotkey, "SetupHotkey"); body.Children.Add(hotkey);
                var searchOptions = new StackPanel { Spacing = 8 };
                Toggle(searchOptions, "Search_StartAtLogin", "SetupStartup", Choices.Startup, value => Choices.Startup = value);
                Toggle(searchOptions, "Setup_Index", "SetupIndexing", Choices.Indexing, value => Choices.Indexing = value);
                body.Children.Add(new Expander { Header = StringTable.Get("Tour_Optional"), Content = searchOptions, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                break;
            case "browsing":
                Combo(body, "DefaultViewTitle", "SetupDefaultView", new[] { (FolderViewKind.Details, "Layout_Details"), (FolderViewKind.Icons, "Layout_LargeIcons"), (FolderViewKind.List, "Layout_List") }, Choices.DefaultView, value => Choices.DefaultView = value);
                Toggle(body, "RestoreLastSessionTitle", "SetupRestoreSession", Choices.RestoreSession, value => Choices.RestoreSession = value);
                var browsingOptions = new StackPanel { Spacing = 8 };
                Toggle(browsingOptions, "Favorites_Show", "SetupFavorites", Choices.Favorites, value => Choices.Favorites = value);
                Toggle(browsingOptions, "Alphabet_QuickNavigation", "SetupAlphabet", Choices.Alphabet, value => Choices.Alphabet = value);
                body.Children.Add(new Expander { Header = StringTable.Get("Tour_Optional"), Content = browsingOptions, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                break;
            case "appearance":
                Combo(body, "AppearanceTitle", "SetupTheme", new[] { (AppThemeKind.System, "ThemeSystem"), (AppThemeKind.Light, "ThemeLight"), (AppThemeKind.Dark, "ThemeDark") }, Choices.Theme, value =>
                {
                    Choices.Theme = value;
                    _preview?.RefreshChoices();
                });
                body.Children.Add(new TextBlock { Text = StringTable.Get("AccentColor"), TextWrapping = TextWrapping.Wrap });
                var colors = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
                for (var index = 0; index < 4; index++) colors.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var presets = AccentPalette.Presets.ToList();
                if (Choices.Accent == AccentKind.Custom)
                    presets.Add(new(AccentKind.Custom, "Accent_Custom", AccentPalette.Resolve(AccentKind.Custom, Choices.CustomAccent)));
                for (var index = 0; index < (presets.Count + 3) / 4; index++) colors.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var buttons = new List<(AccentKind Kind, Button Button)>();
                foreach (var preset in presets)
                {
                    var argb = preset.Argb;
                    var button = new Button { MinWidth = 32, Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Background = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)), Content = preset.Kind == Choices.Accent ? "✓" : "" };
                    var ink = AccentPalette.Foreground(argb);
                    button.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(ink >> 24), (byte)(ink >> 16), (byte)(ink >> 8), (byte)ink));
                    AutomationProperties.SetName(button, StringTable.Get(preset.NameKey));
                    AutomationProperties.SetAutomationId(button, "SetupAccent_" + preset.Kind);
                    SetRow(button, buttons.Count / 4); SetColumn(button, buttons.Count % 4);
                    button.Click += (_, _) => { Choices.Accent = preset.Kind; foreach (var item in buttons) item.Button.Content = item.Kind == preset.Kind ? "✓" : ""; _preview?.RefreshChoices(); };
                    colors.Children.Add(button); buttons.Add((preset.Kind, button));
                }
                body.Children.Add(colors);
                break;
        }
        var settings = new Button { Content = StringTable.Get("Tour_Settings"), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(settings, "TourSettingsEntry");
        settings.Click += (_, _) => SettingsRequested?.Invoke(Page == "search" ? "search" : Page == "appearance" ? "appearance" : "files-folders");
        body.Children.Add(settings);
        _host.Content = body;
        _progress.Text = StringTable.Format("Tour_Progress", PageIndex + 1, _pages.Length);
        AutomationProperties.SetName(_progress, _progress.Text + " · " + StringTable.Get("Tour_" + key + "Title"));
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static void Opening(StackPanel panel, string label, string id, ItemOpeningMode current, Action<ItemOpeningMode> change) =>
        Combo(panel, label, id, Enum.GetValues<ItemOpeningMode>().Select(mode => (mode, "Opening_" + mode)), current, change);

    private static void Combo<T>(StackPanel panel, string label, string id, IEnumerable<(T Value, string Key)> values, T current, Action<T> change) where T : struct
    {
        var box = new ComboBox { Header = StringTable.Get(label), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (value, key) in values)
        {
            var item = new ComboBoxItem { Content = StringTable.Get(key), Tag = value }; box.Items.Add(item);
            if (EqualityComparer<T>.Default.Equals(value, current)) box.SelectedItem = item;
        }
        AutomationProperties.SetAutomationId(box, id); AutomationProperties.SetName(box, StringTable.Get(label));
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: T value }) change(value); };
        panel.Children.Add(box);
    }

    private static void Toggle(StackPanel panel, string label, string id, bool current, Action<bool> change)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = StringTable.Get(label), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var toggle = new ToggleSwitch { IsOn = current, OnContent = "", OffContent = "", MinWidth = 0 };
        SetColumn(toggle, 1); row.Children.Add(toggle);
        AutomationProperties.SetAutomationId(toggle, id); AutomationProperties.SetName(toggle, StringTable.Get(label));
        toggle.Toggled += (_, _) => change(toggle.IsOn); panel.Children.Add(row);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _preview?.Dispose(); _preview = null; _host.Content = null;
        PageChanged = null; PracticeChanged = null; SettingsRequested = null;
    }

    public void CaptureEditors() { if (_hotkey is not null) Choices.Hotkey = _hotkey.Text; }
    public bool TryClosePreview() => _preview?.TryClosePreview() == true;
}
