#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Status;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunToolbarRefinementSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            await Wait(() => TabHost.Content is NavigatorPage p && p.IsLoaded && !p.ViewModel.IsLoading, "navigator");
            AppWindow.Resize(new(2400, 1250));
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "refinement-fixture-" + Guid.NewGuid().ToString("N")[..6])).FullName;
            for (var i = 0; i < 60; i++) File.WriteAllText(Path.Combine(fixture, $"Photo-{i:D2}.txt"), "test");
            Directory.CreateDirectory(Path.Combine(fixture, "Albums"));
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { HiddenToolbarTools = null, GroupingClickCycle = null, DefaultEntryGrouping = EntryGrouping.Mixed });
            AddNavigatorTab(fixture);
            await Wait(() => TabHost.Content is NavigatorPage p && p.ViewModel.AddressText == fixture && !p.ViewModel.IsLoading && p.ViewModel.ItemCount == 61, "fixture");
            var page = (NavigatorPage)TabHost.Content;
            await Wait(() => ((System.Collections.IDictionary)Field(page, "_restoringViews")!).Count == 0, "view restore");
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            var toolbar = (AdaptiveCommandToolbar)page.FindName("Commands");
            Require(page.ViewModel.Sort.EffectiveGrouping == EntryGrouping.Mixed, "Default order was not applied to a new folder.");
            report["DefaultGroupingNewFolder"] = true;
            await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Light);
            await App.AppearanceViewModel.SetFileTypographyAsync("Microsoft YaHei UI", 14, 12);

            foreach (var shown in new[] { true, false })
            {
                await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowHiddenFiles = shown });
                Require(((FontIcon)toolbar.FindName("HiddenFilesIcon")).Glyph == (shown ? "\uE890" : "\uED1A"), "Eye state did not change.");
            }
            report["HiddenFilesTwoDistinctGlyphs"] = true;
            var custom = (Flyout)Call(toolbar, "CreateCustomizationMenu")!;
            custom.ShowAt(toolbar); await Task.Delay(160);
            var checks = PolishDescendants(custom.Content).OfType<CheckBox>().ToArray();
            Require(checks.Length >= 16, "Flat toolbar choices are missing.");
            var copy = checks.Single(c => c.Tag is ToolbarTool.Copy);
            Toggle(copy); await Wait(() => App.ExplorerPreferences.HiddenToolbarTools?.Contains(ToolbarTool.Copy) == true, "hide copy");
            Require(custom.IsOpen, "Customization closed on selection.");
            Toggle(copy); await Wait(() => App.ExplorerPreferences.HiddenToolbarTools?.Contains(ToolbarTool.Copy) != true, "show copy");
            var groupingCheck = checks.Single(c => c.Tag is ToolbarTool.Grouping);
            Toggle(groupingCheck); await Wait(() => ((FrameworkElement)toolbar.FindName("GroupingGroup")).Visibility == Visibility.Collapsed, "hide grouping tool");
            Toggle(groupingCheck); await Wait(() => ((Button)toolbar.FindName("GroupingButton")).Visibility == Visibility.Visible, "restore grouping tool");
            await Task.Delay(180);
            await CapturePopupAsync((FrameworkElement)custom.Content, "refinement-toolbar-light.png"); custom.Hide();
            report["FlatCustomizationStaysOpen"] = true;

            var sort = (Flyout)((Button)toolbar.FindName("SortButton")).Flyout;
            sort.ShowAt((Button)toolbar.FindName("SortButton")); await Task.Delay(160);
            var desc = PolishDescendants(sort.Content).OfType<Button>().Single(b => b.Content as string == StringTable.Get("Sort_Descending"));
            Invoke(desc);
            var size = PolishDescendants(sort.Content).OfType<RadioButton>().Single(b => b.Tag is EntrySortColumn.Size);
            size.IsChecked = true;
            await Wait(() => page.ViewModel.Sort.Column == EntrySortColumn.Size && !page.ViewModel.Sort.Ascending, "sort field retains chosen direction");
            Require(sort.IsOpen, "Sort popup closed.");
            await Task.Delay(180);
            await CapturePopupAsync((FrameworkElement)sort.Content, "refinement-sort-light.png"); sort.Hide();
            report["SortKeepsOpenAndPreservesDirection"] = true;
            toolbar.Width = 360; await Task.Delay(180);
            var more = (Button)toolbar.FindName("MoreButton");
            more.Flyout.ShowAt(more); await Task.Delay(120);
            var overflowSort = (MenuFlyoutItem)Field(toolbar, "_overflowSortPanel")!;
            Require(overflowSort.Visibility == Visibility.Visible, "Narrow toolbar loses sort panel.");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(overflowSort).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => sort.IsOpen, "sort panel from overflow");
            sort.Hide(); await Task.Delay(160); toolbar.Width = double.NaN;
            report["NarrowToolbarUsesSamePersistentSortPanel"] = true;

            page.ViewModel.RestoreSort(EntrySort.Name); toolbar.SetSort(EntrySort.Name);
            Invoke((Button)toolbar.FindName("GroupingButton"));
            Require(page.ViewModel.Sort.EffectiveGrouping == EntryGrouping.Mixed, "Default cycle should enter mixed.");
            Invoke((Button)toolbar.FindName("GroupingButton"));
            Require(page.ViewModel.Sort.EffectiveGrouping == EntryGrouping.FoldersFirst, "Default cycle should skip files first.");
            var grouping = (Flyout)((Button)toolbar.FindName("GroupingMenuButton")).Flyout;
            grouping.ShowAt((Button)toolbar.FindName("GroupingMenuButton")); await Task.Delay(140);
            PolishDescendants(grouping.Content).OfType<RadioButton>().Single(r => r.Tag is EntryGrouping.FilesFirst).IsChecked = true;
            Require(page.ViewModel.Sort.EffectiveGrouping == EntryGrouping.FilesFirst, "Dropdown files first failed.");
            Toggle(PolishDescendants(grouping.Content).OfType<CheckBox>().Single(r => r.Tag is EntryGrouping.FilesFirst));
            await Wait(() => App.ExplorerPreferences.EffectiveGroupingCycle.Count == 3, "include files first in click cycle");
            await CapturePopupAsync((FrameworkElement)grouping.Content, "refinement-grouping-light.png"); grouping.Hide();
            page.ViewModel.RestoreSort(EntrySort.Name with { Grouping = EntryGrouping.Mixed }); toolbar.SetSort(page.ViewModel.Sort);
            Invoke((Button)toolbar.FindName("GroupingButton"));
            Require(page.ViewModel.Sort.EffectiveGrouping == EntryGrouping.FilesFirst, "Custom cycle did not include files first.");
            var restored = new ExplorerPreferencesService(Program.SettingsPath(ExplorerPreferencesService.DefaultFilePath)).Load();
            Require(restored.EffectiveGroupingCycle.Count == 3 && restored.DefaultEntryGrouping == EntryGrouping.Mixed, "Grouping settings were not saved.");
            report["GroupingDefaultCycleCustomCycleDropdownAndSave"] = true;

            await ChooseView("Layout_List");
            surface.SetListZoom(100);
            var rowBefore = Rows().First(); var widthBefore = rowBefore.Width; var fontBefore = ((TextBlock)rowBefore.FindName("NameText")).FontSize;
            SetField(surface, "_pendingZoomDirection", 1); SetField(surface, "_pendingZoomGeneration", Field(surface, "_generation"));
            Call(surface, "ApplyPendingZoom"); await Task.Delay(220);
            Require(surface.LayoutKind == FileLayoutKind.List && surface.ListZoomPercent == 120, "List wheel zoom changed the mode.");
            Require(Rows().First().Width > widthBefore && ((TextBlock)Rows().First().FindName("NameText")).FontSize > fontBefore, "List did not scale.");
            surface.SetListZoom(160); await Task.Delay(160);
            var row = Rows().First();
            var icon = (FrameworkElement)row.FindName("IconFrame"); var name = (FrameworkElement)row.FindName("NameText");
            Require(icon.TransformToVisual(row).TransformPoint(new(0, 0)).X + icon.ActualWidth <= name.TransformToVisual(row).TransformPoint(new(0, 0)).X + 1, "Scaled icon overlaps filename.");
            await Capture(Content, "refinement-list-160.png");
            await ChooseView("Layout_Details");
            row = Rows().First();
            report["RowWidth"] = row.ActualWidth; report["SurfaceWidth"] = surface.ActualWidth;
            Require(row.ActualWidth >= surface.ActualWidth - 50, "Detail backgrounds do not span the viewport.");
            var menu = (MenuFlyout)((Button)toolbar.FindName("ViewMenuButton")).Flyout;
            menu.ShowAt((Button)toolbar.FindName("ViewMenuButton")); await Task.Delay(140);
            Require(menu.Items.OfType<ToggleMenuFlyoutItem>().Count() == 3 && menu.Items.OfType<MenuFlyoutSubItem>().First().Items.Count == 7, "View options missing.");
            await Capture(VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).Select(p => p.Child).OfType<MenuFlyoutPresenter>().Last(), "refinement-view-menu.png"); menu.Hide();
            report["ListZoomKeepsLayoutAndScalesRows"] = true;
            report["IntegratedViewMenuAndFullWidthRows"] = true;

            Call(Field(page, "_fileActions")!, "ShowClipboardNotice", false, 3);
            await Task.Delay(120);
            Require(((TextBlock)_actionNotice!.FindName("MessageText")).Text == StringTable.Format("Clipboard_CopiedCount", 3), "Copy count notice missing.");
            Require(((Button)_actionNotice.FindName("UndoButton")).Visibility == Visibility.Collapsed, "Clipboard copy offers a file undo.");
            await Capture(_actionNotice, "refinement-copy-notice.png");
            Call(Field(page, "_fileActions")!, "ShowClipboardNotice", true, 2);
            Require(((TextBlock)_actionNotice.FindName("MessageText")).Text == StringTable.Format("Clipboard_CutCount", 2), "Cut count notice missing.");
            await Task.Delay(3200); Require(_actionNotice.Visibility == Visibility.Collapsed, "Notice did not auto-dismiss.");
            report["ClipboardNoticesCountsCutCopyAndDismiss"] = true;

            var history = PolishDescendants(page).OfType<OperationHistoryButton>().First();
            var hf = (Flyout)((Button)history.FindName("HistoryButton")).Flyout;
            hf.ShowAt(history); await Task.Delay(160);
            Require(((StackPanel)hf.Content).Padding.Left >= 16, "History card missing insets.");
            await CapturePopupAsync((FrameworkElement)hf.Content, "refinement-history-empty.png"); hf.Hide(); await Task.Delay(250);
            App.FileUndo.Push(FileUndoRecord.Copied([Path.Combine(fixture, "Photo-00.txt")], []));
            hf.ShowAt(history); await Task.Delay(140);
            Require(((Button)history.FindName("UndoButton")).IsEnabled, "History undo unavailable.");
            Require(((Button)history.FindName("OpenButton")).CornerRadius.TopLeft > 0, "Font styling removed rounded button corners.");
            await CapturePopupAsync((FrameworkElement)hf.Content, "refinement-history-filled.png"); hf.Hide();
            report["HistoryEmptyAndPopulatedLayouts"] = true;

            OpenSettings("files-folders"); await Task.Delay(300);
            var settings = PolishDescendants(SettingsHost).OfType<FilesAndFoldersSettingsPage>().First();
            await App.AppearanceViewModel.SetFileTypographyAsync("Consolas", 14, 12);
            await App.AppearanceViewModel.SetFileTypographyAsync("Microsoft YaHei UI", 14, 12);
            await Task.Delay(150);
            Require(((FrameworkElement)settings.FindName("ArchiveProviderActions")).Visibility == Visibility.Collapsed, "Archive actions retain space in auto mode.");
            Require(((ComboBox)settings.FindName("DefaultGroupingBox")).SelectedItem is ComboBoxItem { Tag: "Mixed" }, "Default grouping is not reflected in settings.");
            var fontTexts = PolishDescendants(Content).OfType<TextBlock>().Where(t => t.IsLoaded && t.Text.Length > 0 && !t.FontFamily.Source.Contains("Icons") && t.Tag as string != "FilesMate.ContentTypography").ToArray();
            var wrong = fontTexts.Where(t => t.FontFamily.Source != "Microsoft YaHei UI").Select(t => new { t.Text, Family = t.FontFamily.Source }).ToArray();
            report["WrongFontTexts"] = wrong;
            Require(wrong.Length == 0, "Some UI text did not adopt global font.");
            await Capture(SettingsHost, "refinement-settings-light.png");
            CloseSettings(); await Task.Delay(100);
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            sort.ShowAt((Button)toolbar.FindName("SortButton")); await Task.Delay(150);
            await CapturePopupAsync((FrameworkElement)sort.Content, "refinement-sort-dark.png"); sort.Hide();
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);
            report["GlobalFontIncludingNewSettingsPage"] = true;
            report["Passed"] = true;
            async Task ChooseView(string key)
            {
                var button = (Button)toolbar.FindName("ViewMenuButton");
                var choices = (MenuFlyout)button.Flyout;
                choices.ShowAt(button); await Task.Delay(80);
                var item = choices.Items.OfType<ToggleMenuFlyoutItem>().Single(item => item.Text == StringTable.Get(key));
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(220);
            }
            IEnumerable<FileRow> Rows() => PolishDescendants(surface).OfType<FileRow>().Where(r => r.EntryId >= 0 && r.IsLoaded);
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            try { await Capture(Content, "refinement-failure.png"); } catch { }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "toolbar-refinement-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static object? Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
        static void SetField(object obj, string name, object? value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
        static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Wait(Func<bool> condition, string step) { for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(step); }
        static void Toggle(CheckBox check) => ((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();
        static void Invoke(ButtonBase button)
        {
            if (button is ToggleButton toggle) ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
            else ((IInvokeProvider)new ButtonAutomationPeer((Button)button).GetPattern(PatternInterface.Invoke)).Invoke();
        }
    }
}
#endif
