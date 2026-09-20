#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Settings;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Commands;
using FilesMate.App.Models;
using FilesMate.App.Shortcuts;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private bool _polishSweepStarted;

    private async Task RunPolishSweepAsync()
    {
        var results = new Dictionary<string, string>();
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetColumnSpan(host, 10);
        Grid.SetRowSpan(host, 10);
        ShellRoot.Children.Add(host);
        try
        {
            await Task.Delay(700);
            await Check("LiveSelection", async () =>
            {
                var surface = new FileDetailsSurface();
                host.Content = surface;
                var store = new EntryStore();
                store.Append([new(1, "a.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File), new(2, "b.txt", 2, 1, 1, FileAttributes.Normal, EntryKind.File)]);
                EntryViewIndex Index() => EntryViewIndex.Build(store, EntrySort.Name, new EntryFilter(), NaturalStringComparer.Instance, 1);
                surface.Bind(store, Index(), 1);
                surface.Selection.SelectOnly(1);
                var updates = 0;
                surface.SelectionChanged += (_, _) => updates++;
                store.RemoveByName("a.txt");
                surface.Bind(store, Index(), 1);
                await Task.Delay(100);
                Require(surface.Selection.Count == 0 && surface.Selection.PrimaryId is null && updates > 0,
                    $"Deleted item retained: count={surface.Selection.Count}, notifications={updates}");
                surface.Selection.SelectOnly(2);
                updates = 0;
                surface.Bind(store, Index(), 1);
                Require(updates > 0, "Retained selection did not refresh status after a watcher update");
            });
            await Check("ColumnDragGeometry", async () =>
            {
                var surface = new FileDetailsSurface();
                host.Content = surface;
                surface.SetLayout(FileLayoutKind.Details);
                await Task.Delay(100);
                foreach (var name in new[] { "NameHeader", "ModifiedHeader", "TypeHeader", "SizeHeader" })
                {
                    var button = (Button)surface.FindName(name);
                    var text = Descendants(button).OfType<TextBlock>().First();
                    var before = text.TransformToVisual(surface).TransformPoint(default);
                    var size = text.ActualSize;
                    foreach (var after in new[] { false, true })
                    {
                        typeof(FileDetailsSurface).GetMethod("ShowColumnDropIndicator", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [button, after]);
                        surface.UpdateLayout();
                        Require(text.TransformToVisual(surface).TransformPoint(default) == before && text.ActualSize == size,
                            name + " moved when showing drag feedback");
                    }
                }
            });
            await Check("AppearanceReentry", async () =>
            {
                var vm = App.AppearanceViewModel!;
                var original = vm.Current.ShowStatusBar;
                var page = new AppearancePage();
                await Reenter(page);
                try
                {
                    await vm.SetShowStatusBarAsync(!original);
                    await Task.Delay(150);
                    Require(((ToggleSwitch)page.FindName("StatusBarToggle")).IsOn == !original, "Reopened appearance page stopped reflecting settings changes");
                }
                finally { await vm.SetShowStatusBarAsync(original); }
            });
            await Check("ShortcutsReentry", async () =>
            {
                var page = new ShortcutsSettingsPage();
                await Reenter(page);
                var original = App.Shortcuts[ShortcutAction.OpenTerminal];
                var replacement = new ShortcutGesture(ShortcutKey.F12, ShortcutModifiers.Control | ShortcutModifiers.Shift);
                try
                {
                    var conflict = await App.UpdateShortcutAsync(ShortcutAction.OpenTerminal, replacement);
                    Require(conflict is null, "Fixture shortcut conflict");
                    await Task.Delay(100);
                    var row = Descendants(page).OfType<ShortcutEditorRow>().Single(r => r.Action == ShortcutAction.OpenTerminal);
                    Require(((Button)row.FindName("GestureButton")).Content?.ToString() == replacement.DisplayText, "Reopened shortcut page shows old gesture");
                }
                finally { await App.UpdateShortcutAsync(ShortcutAction.OpenTerminal, original); }
            });
            await Check("SearchSettingsReentry", async () =>
            {
                var page = new SearchSettingsPage();
                await Reenter(page);
                var index = typeof(SearchSettingsPage).GetField("_index", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page);
                Require(App.SearchIndex is not null && ReferenceEquals(index, App.SearchIndex), "Reopened search settings detached from index progress");
            });
            await Check("CompactSortParity", async () =>
            {
                var toolbar = new AdaptiveCommandToolbar { Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
                host.Content = toolbar;
                await Task.Delay(100);
                var normal = (MenuFlyout)((Button)toolbar.FindName("SortButton")).Flyout;
                var more = (MenuFlyout)((Button)toolbar.FindName("MoreButton")).Flyout;
                var sorts = more.Items.OfType<MenuFlyoutItem>().Count(i => i.Name.StartsWith("OverflowSort", StringComparison.Ordinal));
                Require(sorts == normal.Items.OfType<MenuFlyoutItem>().Count(), $"Compact sort has {sorts} of {normal.Items.Count} fields");
                Require(((Button)toolbar.FindName("MoreButton")).Visibility == Visibility.Visible, "Narrow toolbar uses the window width instead of its own width");
                var copiedPaths = 0;
                var invoked = new List<AppCommandId>();
                toolbar.CopyPathClicked += (_, _) => copiedPaths++;
                toolbar.CommandInvoked += (_, id) => invoked.Add(id);
                var dispatch = typeof(AdaptiveCommandToolbar).GetMethod("InvokeOverflowFileCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
                dispatch.Invoke(toolbar, [AppCommandId.CopyPath, new RoutedEventArgs()]);
                Require(copiedPaths == 1 && invoked.Count == 0, "Overflow copy path lost its dedicated handler");
                foreach (var id in new[] { AppCommandId.NewFolder, AppCommandId.NewFile, AppCommandId.Cut, AppCommandId.Copy,
                    AppCommandId.Paste, AppCommandId.Rename, AppCommandId.Share, AppCommandId.Recycle })
                {
                    invoked.Clear();
                    dispatch.Invoke(toolbar, [id, new RoutedEventArgs()]);
                    Require(invoked.SequenceEqual(new[] { id }), $"Overflow command dispatch failed: {id}");
                }
                toolbar.ApplyContext(CommandContext.ForToolbar(1, clipboardHasFiles: true, primaryPath: @"D:\fixture.txt", shareAvailable: true));
                foreach (var width in new[] { 250d, 360, 500, 700, 1100 })
                {
                    toolbar.Width = width;
                    await Task.Delay(100);
                    toolbar.UpdateLayout();
                    var leading = (StackPanel)toolbar.FindName("LeadingGroups");
                    var utilities = (StackPanel)toolbar.FindName("ViewGroup");
                    var start = utilities.TransformToVisual(toolbar).TransformPoint(default).X;
                    Require(start >= -1 && start + utilities.ActualWidth <= width + 1, $"Utility commands clipped at {width}");
                    if (leading.Visibility == Visibility.Visible)
                        Require(leading.ActualWidth <= start + 1, $"Command groups overlap at {width}: {leading.ActualWidth} > {start}");
                    else
                        foreach (var id in new[] { AppCommandId.NewFolder, AppCommandId.NewFile, AppCommandId.Paste, AppCommandId.Copy, AppCommandId.Share })
                            Require(more.Items.OfType<MenuFlyoutItem>().Single(i => i.Name == "OverflowCommand" + id).Visibility == Visibility.Visible,
                                $"Compact toolbar lost {id}");
                }
            });
        }
        finally
        {
            host.Content = null;
            ShellRoot.Children.Remove(host);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "polish-sweep.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task Reenter(UIElement page)
        {
            host.Content = page;
            await Task.Delay(100);
            host.Content = null;
            await Task.Delay(100);
            host.Content = page;
            await Task.Delay(100);
        }
        async Task Check(string name, Func<Task> run)
        {
            try { await run(); results[name] = "Passed"; }
            catch (Exception error) { results[name] = error.ToString(); }
        }
        static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
    }
}
#endif
