#if FILESMATE_UI_TEST
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Theming;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunBrowsingPreferencesSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            await Wait(() => TabHost.Content is NavigatorPage p && p.IsLoaded && !p.ViewModel.IsLoading, "navigator");
            var sidebar = WindowNavigation.Sidebar;
            await Wait(() => Sections().Count >= 4, "sidebar");
            if (Environment.GetEnvironmentVariable("FILESMATE_BROWSING_PREFERENCES_SMOKE") == "restore")
            {
                Require(Sections().Where(s => s.ShowTitle).All(s => !s.IsExpanded), "Collapsed sections were lost after restart.");
                Require(!App.ExplorerPreferences.ShowAlternatingRows, "Row preference was lost after restart.");
                Require(!App.ExplorerPreferences.DefaultSortAscending, "Default sort direction was lost after restart.");
                Require(App.ExplorerPreferences.ShowHiddenFiles, "Hidden files preference was lost after restart.");
                Require(App.ExplorerPreferences.HiddenToolbarTools?.Contains(ToolbarTool.Copy) == true, "Toolbar customization was lost after restart.");
                var restoredToolbarOrder = ToolbarToolCatalog.NormalizeOrder(App.ExplorerPreferences.ToolbarOrder).ToList();
                Require(restoredToolbarOrder.IndexOf(ToolbarTool.Paste) < restoredToolbarOrder.IndexOf(ToolbarTool.Cut), "Toolbar order was lost after restart.");
                report["RestoredAfterProcessRestart"] = true;
            }
            else
            {
                foreach (var section in Sections().Where(s => s.ShowTitle))
                {
                    if (!section.IsExpanded) continue;
                    var header = new Grid { Tag = section };
                    Call(sidebar, "SectionHeader_Tapped", header, null);
                    await Wait(() => !section.IsExpanded, "collapse " + section.Id);
                }
                sidebar.SelectPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                Require(Sections().Where(s => s.ShowTitle).All(s => !s.IsExpanded), "Selection reopened a collapsed section.");
                var previous = Sections()[0]; sidebar.Reload();
                await Wait(() => !ReferenceEquals(previous, Sections()[0]), "sidebar reload");
                Require(Sections().Where(s => s.ShowTitle).All(s => !s.IsExpanded), "Reload lost collapsed sections.");
                sidebar.IsCompact = true; Require(Sections().All(s => s.IsExpanded), "Compact navigation hid icons.");
                sidebar.IsCompact = false; Require(Sections().Where(s => s.ShowTitle).All(s => !s.IsExpanded), "Compact mode overwrote collapsed state.");
                await Task.Delay(100);
                var headers = PolishDescendants(sidebar).OfType<Grid>().Where(grid => grid.Name == "SectionHeader" && grid.Tag is NavigationSection { ShowTitle: true }).ToArray();
                Require(headers.Length >= 4 && headers.All(header => header.ActualHeight <= 34), "Collapsed section headers waste vertical space.");
                await Capture(sidebar, "sidebar-collapsed.png");
                report["CollapsedSidebarHeaderHeight"] = headers.Select(header => header.ActualHeight).ToArray();
                report["SidebarReloadSelectionCompact"] = true;

                var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "browsing-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
                for (var i = 0; i < 75; i++)
                {
                    var letter = (char)('A' + i / 25);
                    File.WriteAllText(Path.Combine(fixture, $"{letter}-File-{i:D2}.txt"), "Preview");
                    Directory.CreateDirectory(Path.Combine(fixture, $"{letter}-Folder-{i:D2}"));
                }
                AddNavigatorTab(fixture);
                await Wait(() => TabHost.Content is NavigatorPage p && p.ViewModel.AddressText == fixture && !p.ViewModel.IsLoading && p.ViewModel.ItemCount == 150, "fixture");
                var page = (NavigatorPage)TabHost.Content;
                await Wait(() => ((System.Collections.IDictionary)Field(page, "_restoringViews")!).Count == 0, "view restore");
                var surface = (FileDetailsSurface)page.FindName("FileSurface");
                surface.SetLayout(FileLayoutKind.Details);
                await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowAlphabetNavigation = true, AlphabetNavigationMinimumItemCount = 0, ShowAlternatingRows = true, HiddenToolbarTools = null });
                await Wait(() => Rows().Count() > 8, "rows");
                Require(Rows().Any(r => ((Border)r.FindName("Stripe")).Visibility == Visibility.Visible), "Default stripes are missing.");
                foreach (var grouping in new[] { EntryGrouping.Mixed, EntryGrouping.FilesFirst, EntryGrouping.FoldersFirst })
                foreach (var ascending in new[] { true, false })
                {
                    var sort = EntrySort.Name with { Grouping = grouping, Ascending = ascending };
                    page.ViewModel.RestoreSort(sort);
                    await Wait(() => page.ViewModel.ViewIndex?.Sort == sort, "grouping");
                    await Task.Delay(100);
                    var index = page.ViewModel.ViewIndex!;
                    var alphabet = (AlphabetNavigation)Field(surface, "_alphabet")!;
                    Require(alphabet.HasBothKinds("B") == (grouping != EntryGrouping.Mixed), "Wrong alphabet group choices.");
                    Call(surface, "JumpLetter", "B", true, false);
                    await Task.Delay(140);
                    var active = (int)Field(surface, "_activeNameSection")!;
                    Require(active >= 0 && index.NameSections[active].Label == "B", "Alphabet jump missed the requested letter.");
                    if (grouping != EntryGrouping.Mixed)
                        Require((page.ViewModel.Store![index[0]].Kind == EntryKind.Directory) == (grouping == EntryGrouping.FoldersFirst), "Group order changed with descending sort.");
                }
                report["AlphabetAllThreeGroupingsBothDirections"] = true;

                // A saved alphabet preference must not revive the rail, its
                // choices or scroll commands outside Details view.
                var scroller = (ScrollViewer)surface.FindName("Scroller");
                foreach (var layout in new[] { FileLayoutKind.Grid, FileLayoutKind.List })
                {
                    surface.SetLayout(FileLayoutKind.Details);
                    Call(surface, "ShowAlphabet");
                    Call(surface, "ShowAlphabetChoices", "B", false);
                    surface.SetLayout(layout);
                    surface.SetAlphabetNavigationPolicy(true, 0, true, false);
                    await Task.Delay(160);
                    var offset = scroller.VerticalOffset;
                    Call(surface, "ShowAlphabet");
                    Call(surface, "ShowAlphabetChoices", "B", false);
                    Call(surface, "JumpLetter", "B", true, false);
                    Call(surface, "ScrollAlphabetTo", 100d);
                    foreach (var name in new[] { "AlphabetOverlay", "AlphabetLetters", "AlphabetBubble", "AlphabetChoices", "AlphabetRange" })
                        Require(((FrameworkElement)surface.FindName(name)).Visibility == Visibility.Collapsed,
                            layout + " retained " + name);
                    Require(((Microsoft.UI.Xaml.Controls.ItemsRepeater)scroller.Content).MinHeight == 0 && scroller.VerticalOffset == offset,
                        layout + " retained alphabet tail space or accepted an alphabet scroll command.");
                    Require(scroller.VerticalScrollBarVisibility == (layout == FileLayoutKind.List
                        ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto), "Native scrollbar did not return.");
                }
                surface.SetLayout(FileLayoutKind.Details);
                Require(((FrameworkElement)surface.FindName("AlphabetOverlay")).Visibility == Visibility.Visible,
                    "Returning to Details lost the alphabet preference.");
                report["AlphabetDetailsOnlyAndViewSwitchCleanup"] = true;

                var toolbar = (AdaptiveCommandToolbar)page.FindName("Commands");
                var hiddenPath = Path.Combine(fixture, "Z-Hidden.txt");
                File.WriteAllText(hiddenPath, "Hidden preview"); File.SetAttributes(hiddenPath, FileAttributes.Hidden);
                await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowHiddenFiles = false });
                await Wait(() => !page.ViewModel.IsLoading && page.ViewModel.ItemCount == 150, "hidden file initially excluded");
                var hiddenButton = (Button)toolbar.FindName("HiddenFilesButton");
                var hiddenPeer = (IInvokeProvider)new ButtonAutomationPeer(hiddenButton).GetPattern(PatternInterface.Invoke);
                hiddenPeer.Invoke();
                await Wait(() => App.ExplorerPreferences.ShowHiddenFiles && !page.ViewModel.IsLoading && page.ViewModel.ItemCount == 151, "show hidden file");
                Require(hiddenButton.Style == (Style)Application.Current.Resources["SelectedCommandBarButtonStyle"], "Hidden files button did not highlight.");
                hiddenPeer.Invoke();
                await Wait(() => !App.ExplorerPreferences.ShowHiddenFiles && !page.ViewModel.IsLoading && page.ViewModel.ItemCount == 150, "hide hidden file");
                report["ToolbarShowsAndHidesRealHiddenFile"] = true;
                var customization = (Flyout)Call(toolbar, "CreateCustomizationMenu")!;
                customization.ShowAt(toolbar); await Task.Delay(100);
                var draftPreferences = JsonSerializer.Serialize(App.ExplorerPreferences);
                var fileCommands = PolishDescendants(customization.Content).OfType<ListView>()
                    .Single(item => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item) == "ToolbarOrderFileCommands");
                fileCommands.SelectedItem = fileCommands.Items.OfType<Grid>().Single(item => item.Tag is ToolbarTool.Copy);
                InvokeToolbarAction(customization, "ToolbarRemove");
                await Wait(() => !fileCommands.Items.OfType<Grid>().Any(item => item.Tag is ToolbarTool.Copy), "remove copy in draft");
                Require(JsonSerializer.Serialize(App.ExplorerPreferences) == draftPreferences, "Toolbar removal persisted before Save.");
                await Capture(customization.Content, "toolbar-customization-horizontal.png");
                InvokeToolbarAction(customization, "ToolbarSave");
                await Wait(() => !customization.IsOpen && ((Button)toolbar.FindName("CopyButton")).Visibility == Visibility.Collapsed, "save custom toolbar hide copy");
                toolbar.ApplyContext(Commands.CommandContext.SingleFile);
                Require(((Button)toolbar.FindName("CopyButton")).Visibility == Visibility.Collapsed, "Changing selection restored a hidden tool.");
                toolbar.Width = 360; await Task.Delay(160);
                var overflowMenu = (MenuFlyout)((Button)toolbar.FindName("MoreButton")).Flyout;
                Require(overflowMenu.Items.OfType<MenuFlyoutItem>().Single(item => item.Name == "OverflowCommandCopy").Visibility == Visibility.Collapsed, "Narrow toolbar restored a hidden tool.");
                Require(((ToggleMenuFlyoutItem)Field(toolbar, "_overflowHiddenFiles")!).Visibility == Visibility.Visible, "Hidden files shortcut is missing from overflow.");
                toolbar.Width = double.NaN; await Task.Delay(100);
                await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { HiddenToolbarTools = Enum.GetValues<ToolbarTool>() });
                Require(PolishDescendants(toolbar).OfType<Button>().Where(button => button.IsLoaded).All(button => !ToolbarEffectivelyVisible(button)), "Hide-all left a visible toolbar button.");
                var restoreMenu = (Flyout)Call(toolbar, "CreateCustomizationMenu")!;
                restoreMenu.ShowAt(toolbar); await Task.Delay(100);
                InvokeToolbarAction(restoreMenu, "ToolbarRestore");
                await Wait(() => PolishDescendants(restoreMenu.Content).OfType<ListView>().Sum(list => list.Items.Count) == ToolbarToolCatalog.All.Count, "restore all draft commands");
                Require(App.ExplorerPreferences.HiddenToolbarTools?.Count == Enum.GetValues<ToolbarTool>().Length, "Restore persisted before Save.");
                InvokeToolbarAction(restoreMenu, "ToolbarSave");
                await Wait(() => !restoreMenu.IsOpen && App.ExplorerPreferences.HiddenToolbarTools is null && App.ExplorerPreferences.ToolbarOrder is null
                    && ((Button)toolbar.FindName("CopyButton")).Visibility == Visibility.Visible, "save restored toolbar defaults");
                report["ToolbarCustomizationDraftHideAllAndSaveRestore"] = true;
                var sortMenu = (Flyout)((Button)toolbar.FindName("GroupingMenuButton")).Flyout;
                sortMenu.ShowAt((FrameworkElement)toolbar.FindName("GroupingMenuButton")); await Task.Delay(120);
                var mixed = PolishDescendants(sortMenu.Content).OfType<RadioButton>().Single(r => r.Tag is EntryGrouping.Mixed);
                mixed.IsChecked = true;
                await Wait(() => page.ViewModel.Sort.EffectiveGrouping == EntryGrouping.Mixed, "grouping menu action");
                sortMenu.Hide(); await Task.Delay(300);
                report["GroupingMenuAction"] = true;

                foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List, FileLayoutKind.Grid })
                {
                    surface.SetLayout(layout); await Task.Delay(160);
                    surface.Selection.SelectAll(page.ViewModel.Store!, page.ViewModel.ViewIndex!);
                    Call(surface, "RefreshRealizedSelection");
                    surface.Focus(FocusState.Programmatic);
                    await PressEscape(surface);
                    await Wait(() => surface.Selection.Count == 0, "Escape selection " + layout);
                }
                surface.SetLayout(FileLayoutKind.Details);
                report["EscapeClearsAllThreeLayouts"] = true;

                OpenSettings("files-folders");
                await Wait(() => PolishDescendants(Content).OfType<FilesAndFoldersSettingsPage>().Any(), "file settings");
                var settings = PolishDescendants(Content).OfType<FilesAndFoldersSettingsPage>().First();
                ((ToggleSwitch)settings.FindName("HiddenFilesToggle")).IsOn = true;
                await Wait(() => App.ExplorerPreferences.ShowHiddenFiles && page.ViewModel.ItemCount == 151, "settings hidden files");
                Require(hiddenButton.Style == (Style)Application.Current.Resources["SelectedCommandBarButtonStyle"], "Settings did not synchronize the toolbar.");
                report["HiddenFilesSettingsSynchronizeToolbar"] = true;
                var stripes = (ToggleSwitch)settings.FindName("AlternatingRowsToggle");
                stripes.IsOn = false;
                await Wait(() => !App.ExplorerPreferences.ShowAlternatingRows, "stripe setting");
                await Wait(() => Rows().All(r => ((Border)r.FindName("Stripe")).Visibility == Visibility.Collapsed), "live stripe refresh");
                ((ComboBox)settings.FindName("DefaultSortDirectionBox")).SelectedIndex = 1;
                await Wait(() => !App.ExplorerPreferences.DefaultSortAscending, "default descending setting");
                CloseSettings();
                report["StripesUpdateLive"] = true;
                var modifiedHeader = (Button)surface.FindName("ModifiedHeader");
                var headerPeer = (IInvokeProvider)new ButtonAutomationPeer(modifiedHeader).GetPattern(PatternInterface.Invoke);
                headerPeer.Invoke(); await Wait(() => page.ViewModel.Sort.Column == EntrySortColumn.Modified && !page.ViewModel.Sort.Ascending, "new field defaults descending");
                headerPeer.Invoke(); await Wait(() => page.ViewModel.Sort.Ascending, "same field toggles ascending");
                report["ConfiguredDefaultSortDirection"] = true;

                // Keep the same menu alive through both theme changes, including a dynamic icon and checked item.
                await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Dark);
                var menu = new MenuFlyout();
                var check = new ToggleMenuFlyoutItem { Text = "选中项 / Selected", IsChecked = true };
                var normal = new MenuFlyoutItem { Text = "普通操作 / Action" };
                var disabled = new MenuFlyoutItem { Text = "不可用 / Disabled", IsEnabled = false };
                var icon = new FontIcon { Glyph = "\uE8D4" };
                ThemeResources.Bind(icon, IconElement.ForegroundProperty, "FilesMate.Selection.AccentBrush"); normal.Icon = icon;
                menu.Items.Add(check); menu.Items.Add(normal); menu.Items.Add(disabled);
                FlyoutTheme.FollowHost(menu); menu.ShowAt(surface);
                foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark, AppThemeKind.Light })
                {
                    await App.AppearanceViewModel!.SetThemeAsync(theme); await Task.Delay(200);
                    var primary = (SolidColorBrush)ThemeResources.Resolve(surface, "FilesMate.Text.PrimaryBrush")!;
                    var disabledInk = ((SolidColorBrush)ThemeResources.Resolve(surface, "FilesMate.Text.DisabledBrush")!).Color;
                    var disabledAlias = ((SolidColorBrush)ThemeResources.Resolve(surface, "MenuFlyoutItemForegroundDisabled")!).Color;
                    var disabledText = PolishDescendants(disabled).OfType<TextBlock>().First(text => text.Text == disabled.Text);
                    var disabledActual = ((SolidColorBrush)disabledText.Foreground).Color;
                    report["DisabledMenuColors" + theme] = new { Expected = disabledInk.ToString(), Alias = disabledAlias.ToString(), Actual = disabledActual.ToString() };
                    Require(disabledActual == disabledInk, "Disabled menu text retained the previous theme.");
                    Require(((SolidColorBrush)check.Foreground).Color == primary.Color, "Checked menu text retained the previous theme.");
                    Require(((SolidColorBrush)normal.Foreground).Color == primary.Color, "Menu text retained the previous theme.");
                    foreach (var (alias, semantic) in new[] {
                        ("ComboBoxBackground", "FilesMate.ComboBox.BackgroundBrush"),
                        ("ComboBoxBorderBrush", "FilesMate.ComboBox.BorderBrush"),
                        ("ComboBoxDropDownBorderBrush", "FilesMate.AddressBar.BorderBrush"),
                        ("TabViewItemHeaderBackground", "FilesMate.Tab.BackgroundBrush"),
                        ("TabViewItemHeaderBackgroundSelected", "FilesMate.Tab.SelectedBrush") })
                        Require(ReferenceEquals(ThemeResources.Resolve(surface, alias), ThemeResources.Resolve(surface, semantic)),
                            $"{alias} retained the startup theme instead of {semantic}.");
                    Require(((SolidColorBrush)((TextBlock)Rows().First().FindName("NameText")).Foreground).Color == primary.Color, "File text retained the previous theme.");
                    var accent = (SolidColorBrush)ThemeResources.Resolve(surface, "FilesMate.Selection.AccentBrush")!;
                    Require(((SolidColorBrush)icon.Foreground).Color == accent.Color, "Dynamic icon retained the previous theme.");
                    await Capture(VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).Select(p => p.Child).OfType<MenuFlyoutPresenter>().First(), "live-menu-" + theme + ".png");
                }
                menu.Hide(); report["LiveMenuAndDynamicThemeBinding"] = true;
                report["ThemeAliasesFollowSkin"] = true;

                var dialog = new ContentDialog { Title = "主题切换验证", Content = new CheckBox { Content = "跟随主题的选项" }, CloseButtonText = "关闭", XamlRoot = surface.XamlRoot };
                ContentDialogTheme.Apply(dialog, surface);
                var pending = dialog.ShowAsync(); await Task.Delay(150);
                await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Dark); await Task.Delay(150);
                Require(dialog.RequestedTheme == ElementTheme.Dark, "Open dialog did not follow theme.");
                await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light); await Task.Delay(150);
                Require(dialog.RequestedTheme == ElementTheme.Light, "Open dialog did not return to light theme.");
                dialog.Hide(); await pending; report["LiveDialogTheme"] = true;

                var positions = new List<object>();
                var root = (FrameworkElement)Content;
                foreach (var point in new[] { new Point(40, 5), new Point(Math.Max(50, surface.ActualWidth - 12), Math.Max(40, surface.ActualHeight - 20)) })
                {
                    var layout = Controls.Menus.FileContextMenuBuilder.BuildLayout(Commands.CommandContext.SingleFile);
                    if (point.X < 100) layout = layout with { Items = layout.Items.Take(3).ToArray() };
                    var flyout = FileContextFlyout.Create(layout, _ => { });
                    FileContextFlyout.ShowAt(flyout, surface, point); await Task.Delay(200);
                    var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p => p.Child).OfType<FlyoutPresenter>().First();
                    var bounds = presenter.TransformToVisual(root).TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
                    var cursor = surface.TransformToVisual(root).TransformPoint(point);
                    positions.Add(new { CursorX = cursor.X, CursorY = cursor.Y, bounds.X, bounds.Y, bounds.Width, bounds.Height, RootHeight = root.ActualHeight });
                    Require(bounds.X >= -1 && bounds.Y >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1, "Context menu escaped the window.");
                    if (cursor.X + bounds.Width < root.ActualWidth && cursor.Y + bounds.Height < root.ActualHeight)
                        Require(Math.Abs(bounds.X - cursor.X) < 2 && Math.Abs(bounds.Y - cursor.Y) < 2, "Context menu is not anchored at the cursor's top-left.");
                    await CapturePopupAsync((FrameworkElement)flyout.Content, point.X < 100 ? "context-at-pointer.png" : "context-at-edge.png");
                    flyout.Hide(); await Task.Delay(90);
                }
                report["ContextMenuBounds"] = positions;
                report["ContextMenuPointerAndEdges"] = true;
                var restartEditor = (Flyout)Call(toolbar, "CreateCustomizationMenu")!;
                restartEditor.ShowAt(toolbar); await Task.Delay(120);
                var restartFiles = PolishDescendants(restartEditor.Content).OfType<ListView>()
                    .Single(item => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item) == "ToolbarOrderFileCommands");
                restartFiles.SelectedItem = restartFiles.Items.OfType<Grid>().Single(item => item.Tag is ToolbarTool.Copy);
                InvokeToolbarAction(restartEditor, "ToolbarRemove");
                await Wait(() => !restartFiles.Items.OfType<Grid>().Any(item => item.Tag is ToolbarTool.Copy), "prepare hidden copy restart draft");
                restartFiles.SelectedItem = restartFiles.Items.OfType<Grid>().Single(item => item.Tag is ToolbarTool.Paste);
                InvokeToolbarAction(restartEditor, "ToolbarMoveLeft");
                await Wait(() => restartFiles.Items.OfType<Grid>().Select(item => (ToolbarTool)item.Tag).Take(3)
                    .SequenceEqual(new[] { ToolbarTool.New, ToolbarTool.Paste, ToolbarTool.Cut }), "prepare toolbar order restart draft");
                InvokeToolbarAction(restartEditor, "ToolbarSave");
                await Wait(() => !restartEditor.IsOpen && App.ExplorerPreferences.HiddenToolbarTools?.Contains(ToolbarTool.Copy) == true,
                    "save toolbar state for process restart");
                report["ToolbarRestartStateSavedThroughEditor"] = true;
                await Capture(Content, "browsing-light-no-stripes.png");
                IEnumerable<FileRow> Rows() => PolishDescendants(surface).OfType<FileRow>().Where(r => r.EntryId >= 0 && r.IsLoaded);
            }
            report["Passed"] = true;
            IReadOnlyList<NavigationSection> Sections() => ((IEnumerable<NavigationSection>)Field(sidebar, "_sections")!).ToArray();
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            try { await Capture(Content, "browsing-failure.png"); } catch { }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "browsing-preferences-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static void InvokeToolbarAction(Flyout editor, string id)
        {
            var button = PolishDescendants(editor.Content).OfType<Button>().Single(item => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item) == id);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        }
        static bool ToolbarEffectivelyVisible(FrameworkElement element)
        {
            for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is FrameworkElement { Visibility: Visibility.Collapsed }) return false;
            return true;
        }
        static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> condition, string step)
        { for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(step); }
        async Task PressEscape(FileDetailsSurface target)
        {
            var hwnd = NativeHandle;
            AppWindow.Move(new(80, 80)); AppWindow.Show();
            Activate();
            var currentThread = BrowsingGetCurrentThreadId();
            var foregroundThread = BrowsingGetWindowThreadProcessId(BrowsingGetForegroundWindow(), out _);
            var attached = currentThread != foregroundThread && BrowsingAttachThreadInput(currentThread, foregroundThread, true);
            try { BrowsingSetForegroundWindow(hwnd); }
            finally { if (attached) BrowsingAttachThreadInput(currentThread, foregroundThread, false); }
            target.Focus(FocusState.Programmatic); await Task.Delay(150);
            var focus = BrowsingGetFocus();
            BrowsingGetWindowThreadProcessId(focus, out var processId);
            Require(processId == Environment.ProcessId, "Test input target does not belong to this process.");
            // Route native keyboard messages only to our focused XAML island, even if the user is typing in another app.
            BrowsingPostMessage(focus, 0x100, 0x1B, 0x00010001);
            BrowsingPostMessage(focus, 0x101, 0x1B, unchecked((nint)(int)0xC0010001));
            await Task.Delay(100);
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool BrowsingSetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint BrowsingGetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint BrowsingGetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")] private static extern uint BrowsingGetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "AttachThreadInput")] private static extern bool BrowsingAttachThreadInput(uint thread, uint otherThread, bool attach);
    [DllImport("user32.dll", EntryPoint = "GetFocus")] private static extern nint BrowsingGetFocus();
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool BrowsingPostMessage(nint hwnd, uint message, nuint wparam, nint lparam);
}
#endif
