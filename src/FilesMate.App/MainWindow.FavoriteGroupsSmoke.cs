#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.Text.Json;
using FilesMate.App.Controls.Favorites;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunFavoriteGroupsSmokeAsync()
    {
        var results = new Dictionary<string, object>();
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1280, 850));
            await App.Favorites.RemoveManyAsync(App.Favorites.Entries.Select(entry => entry.Id));
            var folder = Path.Combine(AppContext.BaseDirectory, "group-fixture");
            Directory.CreateDirectory(folder);
            App.SetFavoritesBarEnabled(true);
            await App.Favorites.CreateGroupAsync("Work");
            var root = App.Favorites.Entries.Last();
            await App.Favorites.CreateGroupAsync("Projects", root.Id);
            var child = App.Favorites.Entries.Last();
            await App.Favorites.CreateGroupAsync("2026", child.Id);
            var leaf = App.Favorites.Entries.Last();
            await App.Favorites.AddAsync([(@"Z:\not-connected\sample.exe", false)], leaf.Id);
            AddNavigatorTab(folder);
            await WaitFor(() => WindowFavorites.IsLoaded && FindDescendant<Button>(WindowFavorites, b => AutomationProperties.GetName(b) == "Work") is not null);
            var rootButton = FindDescendant<Button>(WindowFavorites, b => AutomationProperties.GetName(b) == "Work")!;
            var stopwatch = Stopwatch.StartNew();
            Invoke(rootButton);
            await WaitFor(() => Popup<MenuFlyoutSubItem>(item => item.Text == "Projects") is not null);
            results["OpenRootMilliseconds"] = stopwatch.ElapsedMilliseconds;
            var projects = Popup<MenuFlyoutSubItem>(item => item.Text == "Projects")!;
            var year = projects.Items.OfType<MenuFlyoutSubItem>().Single(item => item.Text == "2026");
            Require(year.Items.Count == 0, "Menu eagerly populated unopened descendants");
            Expand(projects);
            await WaitFor(() => year.IsLoaded && year.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "sample.exe"));
            Expand(year);
            await WaitFor(() => Popup<MenuFlyoutItem>(item => item.Text == "sample.exe") is not null);
            Require(Popup<MenuFlyoutItem>(item => item.Text == "sample.exe")!.Icon is ImageIcon { Source: not null }, "Offline favorite has no immediate icon");
            results["NestedMenusAndOfflineIcon"] = true;
            Collapse(year); Collapse(projects);
            Invoke(Popup<MenuFlyoutItem>(item => item.Text == StringTable.Get("Favorites_NewSubgroup"))!);
            await WaitFor(() => Popup<FavoriteNameDialog>(_ => true) is not null);
            var dialog = Popup<FavoriteNameDialog>(_ => true)!;
            var input = FindDescendant<TextBox>(dialog, b => AutomationProperties.GetAutomationId(b) == "FavoriteGroupName")!;
            var confirm = FindDescendant<Button>(dialog, b => AutomationProperties.GetAutomationId(b) == "FavoriteGroupConfirm")!;
            var surface = FindDescendant<Border>(dialog, b => b.Name == "BackgroundElement")!;
            var gap = confirm.TransformToVisual(dialog).TransformPoint(new()).Y - input.TransformToVisual(dialog).TransformPoint(new()).Y - input.ActualHeight;
            Require(surface.ActualHeight <= 220 && gap <= 24, "Name dialog still has an oversized gap");
            results["CompactDialog"] = new { Height = surface.ActualHeight, Gap = gap, ButtonWidth = confirm.ActualWidth };
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                dialog.RequestedTheme = theme;
                ((FrameworkElement)dialog.Content).RequestedTheme = theme;
                await Task.Delay(100);
                await Capture(surface, "group-name-" + theme + ".png");
            }
            input.Text = ""; Invoke(confirm); await Task.Delay(100);
            Require(dialog.IsLoaded && confirm.IsEnabled, "Empty name did not keep a usable editor");
            input.Text = "Created in menu"; Invoke(confirm);
            await WaitFor(() => Popup<FavoriteNameDialog>(_ => true) is null);
            var created = App.Favorites.Entries.Single(entry => entry.Name == "Created in menu");
            Require(created.GroupId == root.Id, "Menu created subgroup under the wrong parent");
            results["CreateSubgroupFromMenu"] = true;

            rootButton = FindDescendant<Button>(WindowFavorites, b => AutomationProperties.GetName(b) == "Work")!;
            Invoke(rootButton);
            await WaitFor(() => Popup<MenuFlyoutItem>(item => item.Text == StringTable.Get("Favorites_ManageMenu")) is not null);
            Invoke(Popup<MenuFlyoutItem>(item => item.Text == StringTable.Get("Favorites_ManageMenu"))!);
            await WaitFor(() => Popup<FavoritesManager>(_ => true) is not null);
            var manager = Popup<FavoritesManager>(_ => true)!;
            var groups = FindDescendant<ListView>(manager, item => item.Name == "Groups")!;
            var rows = groups.Items.OfType<FavoritesManager.GroupRow>().ToArray();
            Require(rows.Single(item => item.Id == leaf.Id).Depth == 2, "Manager lost group depth");
            Invoke(FindDescendant<Button>(manager, item => item.Name == "NewGroupButton")!);
            var editor = FindDescendant<TextBox>(manager, item => item.Name == "NameEditor")!;
            editor.Text = "Created in manager";
            var editArea = FindDescendant<Grid>(manager, item => item.Name == "Editor")!;
            Invoke(FindDescendant<Button>(editArea, item => Equals(item.Content, StringTable.Get("Confirm")))!);
            await WaitFor(() => App.Favorites.Entries.Any(item => item.Name == "Created in manager"));
            Require(App.Favorites.Entries.Single(item => item.Name == "Created in manager").GroupId == root.Id, "Manager created subgroup at root");
            groups.SelectedItem = groups.Items.OfType<FavoritesManager.GroupRow>().Single(item => item.Id == leaf.Id);
            await Task.Delay(100);
            var entries = FindDescendant<ListView>(manager, item => item.Name == "Entries")!;
            Require(entries.Items.OfType<FavoritesManager.EntryRow>().Single().Name == "sample.exe", "Selecting a nested group shows the wrong contents");
            await Capture(manager, "nested-groups-manager.png");
            Invoke(FindDescendant<Button>(manager, item => item.Name == "ManagerCloseButton")!);
            results["ManagerHierarchyAndCreation"] = true;
            results["Passed"] = true;
        }
        catch (Exception error) { results["Passed"] = false; results["Error"] = error.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "favorite-groups-smoke.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Invoke(FrameworkElement element) => ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(element).GetPattern(PatternInterface.Invoke)).Invoke();
        static void Expand(MenuFlyoutSubItem item) => ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(item).GetPattern(PatternInterface.ExpandCollapse)).Expand();
        static void Collapse(MenuFlyoutSubItem item) => ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(item).GetPattern(PatternInterface.ExpandCollapse)).Collapse();
        T? Popup<T>(Func<T, bool> match) where T : DependencyObject => VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)Content).XamlRoot)
            .Select(p => p.Child is T item && match(item) ? item : FindDescendant<T>(p.Child, match)).FirstOrDefault(item => item is not null);
        static async Task WaitFor(Func<bool> ready)
        {
            for (var i = 0; i < 200; i++) { if (ready()) { await Task.Delay(100); return; } await Task.Delay(50); }
            throw new TimeoutException("Group UI did not settle");
        }
    }
}
#endif
