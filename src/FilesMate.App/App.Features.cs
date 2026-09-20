using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Services;
using FilesMate.App.Models;
using FilesMate.App.Localization;
using Microsoft.UI.Xaml.Media;
using FilesMate.App.Theming;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public partial class App
{
    private static string FeatureProfile => Path.GetDirectoryName(Program.SettingsPath(FeatureSetup.FilePath()))!;
    internal static FeatureSetup Features { get; private set; } = FeatureSetup.Load(FeatureProfile);
    internal static FavoritesStore Favorites { get; } = new(Path.Combine(FeatureProfile, "favorites.json"));
    internal static event EventHandler? FeaturesChanged;
    private static bool _setupShowing;

    internal static void SetFavoritesManagerHeight(double height)
    {
        var next = Features with { FavoritesManagerHeight = height };
        next.Save(FeatureProfile);
        Features = next;
    }

    internal static void SetFavoritesBarEnabled(bool enabled)
    {
        var next = Features with { FavoritesBarEnabled = enabled };
        next.Save(FeatureProfile);
        Features = next;
        FeaturesChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void SetSidebarCollapsed(bool collapsed)
    {
        var next = Features with { SidebarCollapsed = collapsed };
        next.Save(FeatureProfile);
        Features = next;
        FeaturesChanged?.Invoke(null, EventArgs.Empty);
    }

    private void ScheduleFeatureSetup(MainWindow window)
    {
        if (Features.Completed || (Program.IsUiTestBuild && Environment.GetEnvironmentVariable("FILESMATE_TEST_SETUP") != "1")) return;
        if (window.Content is not FrameworkElement root) return;
        if (root.IsLoaded) _ = ShowFeatureSetupAsync(root);
        else
        {
            RoutedEventHandler? handler = null;
            handler = async (_, _) => { root.Loaded -= handler; await ShowFeatureSetupAsync(root); };
            root.Loaded += handler;
        }
    }

    internal static async Task ShowFeatureSetupAsync(FrameworkElement root)
    {
        if (_setupShowing) return;
        _setupShowing = true;
        try
        {
            var settings = SearchIndexSettingsStore?.Load() ?? Models.SearchIndexSettings.Default;
            var global = GlobalSearchConfiguration.Load(FeatureProfile);
            // Extend the scrollbar into the dialog's 24-DIP padding, leaving 8 DIP at
            // the outer edge. Compensate inside so option widths and alignment stay stable.
            var panel = new StackPanel { Spacing = 14, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 36, 0) };
            panel.Children.Add(new TextBlock { Text = Loc.Get("Setup_Lead"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            ToggleSwitch Choice(string title, string description, bool enabled)
            {
                var card = new Grid { ColumnSpacing = 18, Padding = new Thickness(0, 4, 0, 4) };
                card.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var text = new StackPanel { Spacing = 4 };
                text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 });
                card.Children.Add(text);
                var toggle = new ToggleSwitch { IsOn = enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, title);
                Grid.SetColumn(toggle, 1);
                card.Children.Add(toggle);
                panel.Children.Add(card);
                return toggle;
            }
            var indexing = Choice(Loc.Get("Setup_Index"), Loc.Get("Setup_IndexDescription"), settings.AutoRefresh);
            var search = Choice(Loc.Get("GlobalSearch"), Loc.Get("Setup_SearchDescription"), global.Enabled);
            var hotkey = new ComboBox { Header = Loc.Get("SearchShortcut"), IsEditable = true, PlaceholderText = Loc.Get("Shortcut_ExampleK"), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = search.IsOn };
            foreach (var combination in new[] { global.Hotkey, "Alt+Space", "Ctrl+Alt+Space", "Ctrl+Shift+Space" }.Distinct()) hotkey.Items.Add(combination);
            hotkey.SelectedIndex = 0;
            hotkey.Text = global.Hotkey;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(hotkey, "SetupHotkey");
            var hotkeyRow = new Grid { ColumnSpacing = 8 };
            hotkeyRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            hotkeyRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            hotkeyRow.Children.Add(hotkey);
            var captureHotkey = new Button { Content = Loc.Get("Shortcut_Record"), VerticalAlignment = VerticalAlignment.Bottom, IsEnabled = search.IsOn };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(captureHotkey, "SetupCaptureHotkey");
            Grid.SetColumn(captureHotkey, 1);
            hotkeyRow.Children.Add(captureHotkey);
            panel.Children.Add(hotkeyRow);
            var capturing = false;
            void EndCapture() { capturing = false; IsShortcutCaptureActive = false; captureHotkey.Content = Loc.Get("Shortcut_Record"); }
            captureHotkey.Click += (_, _) => { capturing = true; IsShortcutCaptureActive = true; captureHotkey.Content = Loc.Get("Shortcut_PressCombination"); captureHotkey.Focus(FocusState.Programmatic); };
            captureHotkey.LostFocus += (_, _) => { if (capturing) EndCapture(); };
            captureHotkey.KeyDown += (_, e) =>
            {
                if (!capturing) return;
                e.Handled = true;
                if (e.Key == Windows.System.VirtualKey.Escape) { EndCapture(); return; }
                bool Down(Windows.System.VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (e.Key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Menu or Windows.System.VirtualKey.Shift) return;
                if (Down(Windows.System.VirtualKey.LeftWindows) || Down(Windows.System.VirtualKey.RightWindows)) { captureHotkey.Content = Loc.Get("Shortcut_WinUnsupported"); return; }
                var modifiers = (Down(Windows.System.VirtualKey.Menu) ? 1u : 0u) | (Down(Windows.System.VirtualKey.Control) ? 2u : 0u) | (Down(Windows.System.VirtualKey.Shift) ? 4u : 0u);
                var shortcut = new SearchHotkey(modifiers, (uint)e.Key);
                if (!SearchHotkey.TryParse(shortcut.ToString(), out var parsed)) { captureHotkey.Content = Loc.Get("Shortcut_TryAnother"); return; }
                hotkey.Text = parsed.ToString();
                EndCapture();
            };
            var startup = Choice(Loc.Get("Search_StartAtLogin"), Loc.Get("Search_StartAtLoginHint"), global.StartAtLogin);
            search.Toggled += (_, _) => { startup.IsEnabled = search.IsOn; hotkey.IsEnabled = search.IsOn; captureHotkey.IsEnabled = search.IsOn; if (capturing) EndCapture(); };
            startup.IsEnabled = search.IsOn;
            var favorites = Choice(Loc.Get("Favorites_Show"), Loc.Get("Favorites_SetupHint"), Features.Completed ? Features.FavoritesBarEnabled : true);
            var alphabet = Choice(Loc.Get("Alphabet_QuickNavigation"), Loc.Get("Alphabet_SetupHint"), ExplorerPreferences.ShowAlphabetNavigation);
            var appearance = AppearanceViewModel?.Current ?? AppearanceSettings.Default;
            var theme = new ComboBox { Header = Loc.Get("AppearanceTitle"), HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = (int)appearance.Theme };
            theme.Items.Add(Loc.Get("ThemeSystem"));
            theme.Items.Add(Loc.Get("ThemeLight"));
            theme.Items.Add(Loc.Get("ThemeDark"));
            theme.SelectedIndex = (int)appearance.Theme;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(theme, "SetupTheme");
            panel.Children.Add(theme);
            panel.Children.Add(new TextBlock { Text = Loc.Get("AccentColor"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var accent = appearance.Accent;
            var swatches = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (var i = 0; i < 8; i++) swatches.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var choices = AccentPalette.Presets.ToList();
            if (accent == AccentKind.Custom) choices.Add(new(accent, "Accent_Custom", AccentPalette.Resolve(accent, appearance.CustomAccent)));
            for (var i = 0; i < (choices.Count + 7) / 8; i++) swatches.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var accentButtons = new List<(AccentKind Kind, Button Button)>();
            foreach (var swatch in choices)
            {
                var color = Windows.UI.Color.FromArgb((byte)(swatch.Argb >> 24), (byte)(swatch.Argb >> 16), (byte)(swatch.Argb >> 8), (byte)swatch.Argb);
                var button = new Button { Width = 38, Height = 34, Padding = new Thickness(0), CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(color),
                    Content = swatch.Kind == accent ? "✓" : "", Foreground = new SolidColorBrush(swatch.Kind is AccentKind.Gold or AccentKind.Mint ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White) };
                var name = StringTable.Get(swatch.NameKey);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "SetupAccent_" + swatch.Kind);
                ToolTipService.SetToolTip(button, name);
                Grid.SetRow(button, accentButtons.Count / 8);
                Grid.SetColumn(button, accentButtons.Count % 8);
                button.Click += (_, _) => { accent = swatch.Kind; foreach (var choice in accentButtons) choice.Button.Content = choice.Kind == accent ? "✓" : ""; };
                accentButtons.Add((swatch.Kind, button));
                swatches.Children.Add(button);
            }
            panel.Children.Add(swatches);
            var errorNotice = new InfoBar { IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Error, Margin = new Thickness(0, 12, 0, 0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(errorNotice, "SetupError");
            var changeHotkey = new Button { Content = Loc.Get("Shortcut_Change") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(changeHotkey, "SetupChangeHotkey");
            var scroller = new ScrollViewer
            {
                Content = panel, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, -16, 0),
                HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroller, "SetupScrollViewer");
            changeHotkey.Click += (_, _) =>
            {
                var position = hotkey.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(0, 0));
                scroller.ChangeView(null, Math.Max(0, position.Y - 8), null, disableAnimation: true);
            };
            // The settings page may be taller than the window. Size against the actual viewport.
            void ResizeContent() => scroller.MaxHeight = Math.Clamp(root.XamlRoot.Size.Height - 220 - (errorNotice.IsOpen ? errorNotice.ActualHeight + 12 : 0), 80, 660);
            void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeContent();
            ResizeContent();
            var content = new Grid();
            content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            content.Children.Add(scroller);
            Grid.SetRow(errorNotice, 1);
            content.Children.Add(errorNotice);
            errorNotice.SizeChanged += (_, _) => ResizeContent();
            var dialog = new ContentDialog { Title = Loc.Get("Setup_Title"), Content = content, XamlRoot = root.XamlRoot,
                PrimaryButtonText = Loc.Get("Setup_Start"), CloseButtonText = Loc.Get("Setup_Later"), DefaultButton = ContentDialogButton.Primary };
            ContentDialogTheme.Apply(dialog, root);
            theme.SelectionChanged += (_, _) => dialog.RequestedTheme = theme.SelectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => root.RequestedTheme };
            async Task<bool> SaveChoicesAsync(bool withoutSearch)
            {
                try
                {
                    errorNotice.IsOpen = false;
                    var enabled = search.IsOn && !withoutSearch;
                    var shortcutText = enabled ? hotkey.Text : global.Hotkey;
                    if (!SearchHotkey.TryParse(shortcutText, out var shortcut))
                    {
                        errorNotice.Title = Loc.Get("Shortcut_Invalid");
                        errorNotice.Message = Loc.Get("Shortcut_InvalidHint");
                        errorNotice.ActionButton = changeHotkey;
                        errorNotice.IsOpen = true;
                        dialog.SecondaryButtonText = Loc.Get("Setup_SkipSearch");
                        ResizeContent();
                        return false;
                    }
                    var nextGlobal = global with { Enabled = enabled, StartAtLogin = enabled && startup.IsOn, Hotkey = shortcut.ToString() };
                    SearchHostReply reply;
                    if (Program.IsUiTestBuild)
                    {
                        reply = enabled && Environment.GetEnvironmentVariable("FILESMATE_TEST_SETUP_HOTKEY_CONFLICT") == "1" && nextGlobal.Hotkey == "Alt+Space"
                            ? new(false, "测试快捷键被占用", ErrorCode: "HotkeyConflict") : new(true, "");
                        if (reply.Ok) GlobalSearchConfiguration.Save(nextGlobal, FeatureProfile);
                    }
                    else
                    {
                        reply = await GlobalSearchService.ApplyAsync(nextGlobal);
                    }
                    if (!reply.Ok)
                    {
                        errorNotice.Title = reply.IsHotkeyConflict ? Loc.Format("Shortcut_InUse", nextGlobal.Hotkey) : Loc.Get("Search_EnableFailed");
                        errorNotice.Message = reply.IsHotkeyConflict
                            ? Loc.Get("Search_EnableFailedHint")
                            : reply.Message + Loc.Get("Search_ContinueWithout");
                        errorNotice.ActionButton = reply.IsHotkeyConflict ? changeHotkey : null;
                        errorNotice.IsOpen = true;
                        dialog.SecondaryButtonText = Loc.Get("Setup_SkipSearch");
                        ResizeContent();
                        return false;
                    }
                    // Failed hotkey registration must not partially save the index choice.
                    if (SearchIndexSettingsStore is { } store) await store.SaveAsync(settings with { AutoRefresh = indexing.IsOn });
                    await SetExplorerPreferencesAsync(ExplorerPreferences with { ShowAlphabetNavigation = alphabet.IsOn });
                    if (AppearanceViewModel is { } appearanceModel)
                    {
                        await appearanceModel.SetThemeAndAccentAsync((AppThemeKind)Math.Max(0, theme.SelectedIndex), accent);
                        if (appearanceModel.ErrorText is { } appearanceError) throw new IOException(appearanceError);
                    }
                    var next = Features with { Completed = true, FavoritesBarEnabled = favorites.IsOn };
                    next.Save(FeatureProfile);
                    Features = next;
                    FeaturesChanged?.Invoke(null, EventArgs.Empty);
                    if (!indexing.IsOn) SearchIndex?.Cancel();
                    if (indexing.IsOn && !Program.IsUiTestBuild) _ = TryAutoIndexAsync(CancellationToken.None);
                    return true;
                }
                catch (Exception error)
                {
                    errorNotice.Title = Loc.Get("Settings_SaveFailed");
                    errorNotice.Message = error.Message;
                    errorNotice.IsOpen = true;
                    ResizeContent();
                    return false;
                }
            }
            dialog.PrimaryButtonClick += async (_, e) =>
            {
                var deferral = e.GetDeferral();
                try { e.Cancel = !await SaveChoicesAsync(withoutSearch: false); }
                finally { deferral.Complete(); }
            };
            dialog.SecondaryButtonClick += async (_, e) =>
            {
                var deferral = e.GetDeferral();
                try { e.Cancel = !await SaveChoicesAsync(withoutSearch: true); }
                finally { deferral.Complete(); }
            };
#if FILESMATE_UI_TEST
            if (Environment.GetEnvironmentVariable("FILESMATE_TEST_SETUP_RECOVERY") is { } recovery)
            {
                dialog.Opened += async (_, _) =>
                {
                    try
                    {
                        await Task.Delay(300);
                        if (await SaveChoicesAsync(false)) throw new InvalidOperationException("Conflict unexpectedly completed setup");
                        await Task.Delay(300);
                        content.UpdateLayout();
                        var scrollBottom = scroller.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, scroller.ActualHeight)).Y;
                        var errorTop = errorNotice.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                        if (!errorNotice.IsOpen || dialog.SecondaryButtonText.Length == 0 || scrollBottom > errorTop + 1)
                            throw new InvalidOperationException($"Conflict layout invalid: scroll bottom={scrollBottom}, notice top={errorTop}");
                        var gutter = scroller.ActualWidth - panel.TransformToVisual(scroller).TransformPoint(new Windows.Foundation.Point(panel.ActualWidth, 0)).X;
                        if (gutter < 19) throw new InvalidOperationException($"Scrollbar gutter missing: {gutter}");
                        var jump = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(changeHotkey)
                            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
                        jump.Invoke();
                        await Task.Delay(300);
                        if (scroller.VerticalOffset <= 0) throw new InvalidOperationException("Change-shortcut action did not scroll to field");
                        var skip = recovery == "skip";
                        if (!skip) hotkey.Text = "Ctrl+Alt+K";
                        if (!await SaveChoicesAsync(skip)) throw new InvalidOperationException("Recovery did not complete");
                        var saved = GlobalSearchConfiguration.Load(FeatureProfile);
                        if (!Features.Completed || saved.Enabled == skip || !skip && saved.Hotkey != "Ctrl+Alt+K")
                            throw new InvalidOperationException("Recovery choices not persisted");
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "setup-recovery-smoke.json"),
                            System.Text.Json.JsonSerializer.Serialize(new { Passed = true, Recovery = recovery, Gutter = gutter, ScrollBottom = scrollBottom, ErrorTop = errorTop }));
                    }
                    catch (Exception error)
                    {
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "setup-recovery-smoke.json"),
                            System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() }));
                    }
                    finally { dialog.Hide(); }
                };
            }
#endif
            root.XamlRoot.Changed += RootChanged;
            try { await dialog.ShowAsync(); }
            finally { if (capturing) EndCapture(); root.XamlRoot.Changed -= RootChanged; }
        }
        catch (Exception error) { LogFailure("Feature setup", error); }
        finally { _setupShowing = false; }
    }
}
