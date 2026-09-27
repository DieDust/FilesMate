#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.Favorites;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunFavoriteStarSmokeAsync()
    {
        var results = new Dictionary<string, object>();
        MainWindow? other = null;
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1280, 850));
            await App.Favorites.RemoveManyAsync(App.Favorites.Entries.Select(entry => entry.Id));
            var fixture = Path.Combine(AppContext.BaseDirectory, "favorite-star-fixture");
            var alpha = Path.Combine(fixture, "Alpha"); var beta = Path.Combine(fixture, "Beta");
            Directory.CreateDirectory(alpha); Directory.CreateDirectory(beta);
            App.SetFavoritesBarEnabled(true);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { OpenFoldersInNewTab = false });
            AddNavigatorTab(alpha);
            var page = await Ready(this, alpha);
            var originalTab = (TabViewItem)Tabs.SelectedItem;
            var star = Star(this);
            AssertStar(star, false);
            Invoke(star);
            await WaitFor(() => App.Favorites.FindFolders(alpha).Count == 1 && Popup<TextBox>("FavoriteName") is not null);
            AssertStar(star, true);
            var saved = App.Favorites.FindFolders(alpha).Single();
            Popup<Button>("FavoriteSave")!.Focus(FocusState.Programmatic);
            Invoke(Popup<Button>("FavoriteSave")!);
            await WaitFor(() => Popup<TextBox>("FavoriteName") is null);
            results["AddFillsStar"] = true;

            await App.Favorites.CreateGroupAsync("Work");
            var group = App.Favorites.Entries.Single(entry => entry.IsGroup && entry.Name == "Work");
            var child = await App.Favorites.CreateGroupAsync("项目资料", group.Id);
            var leaf = await App.Favorites.CreateGroupAsync("设计素材", child.Id);
            await App.Favorites.CreateGroupAsync("项目资料");
            Invoke(star);
            await WaitFor(() => Popup<TextBox>("FavoriteName") is not null);
            Require(App.Favorites.FindFolders(alpha).Count == 1, "Clicking filled star duplicated the favorite");
            Popup<TextBox>("FavoriteName")!.Text = "My Alpha";
            var picker = Popup<FavoriteGroupPicker>("FavoriteGroup")!;
            var tree = FindDescendant<TreeView>(picker, _ => true)!;
            Require(tree.RootNodes.Count == 1 && tree.RootNodes[0].Children.Count == 2, "Picker flattened the hierarchy");
            Require(tree.RootNodes[0].Children[0].Children.Count == 0, "Picker eagerly realized unopened subgroups");
            Invoke(FindDescendant<Button>(picker, b => b.Name == "LocationButton")!);
            picker.SelectGroup(leaf.Id);
            await Task.Delay(150);
            Require(tree.SelectedNode.Parent.Parent.Content is FavoriteGroupPicker.GroupChoice { Id: var parent } && parent == group.Id, "Wrong selected ancestors");
            var content = Popup<StackPanel>("FavoriteEditor")!;
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                content.RequestedTheme = theme;
                // System backdrops are not included in RenderTargetBitmap.
                content.Background = new SolidColorBrush(FilesMate.App.Controls.Glass.FrostedBackdrop.FloatingColor(theme == ElementTheme.Dark));
                await Task.Delay(150);
                Require(FindDescendant<TextBlock>(tree, text => text.Text == "设计素材") is not null, "Tree row template did not display group names");
                await Capture(content, "favorite-tree-" + theme + ".png");
            }
            content.RequestedTheme = ElementTheme.Dark;
            var rootNode = tree.RootNodes[0];
            var rootContainer = (TreeViewItem)tree.ContainerFromNode(rootNode);
            ((ISelectionItemProvider)FrameworkElementAutomationPeer.CreatePeerForElement(rootContainer).GetPattern(PatternInterface.SelectionItem)).Select();
            await Task.Delay(100);
            Require(picker.SelectedGroupId is null, "Tree selection did not update the location");
            picker.SelectGroup(child.Id);
            Invoke(FindDescendant<Button>(picker, b => b.Name == "NewGroupButton")!);
            var newName = FindDescendant<TextBox>(picker, b => b.Name == "NewGroupName")!;
            var create = FindDescendant<Button>(picker, b => b.Name == "CreateButton")!;
            newName.Text = ""; Invoke(create); await Task.Delay(100);
            Require(!picker.IsSaving && create.IsEnabled && FindDescendant<TextBlock>(picker, b => b.Name == "ErrorText")!.Visibility == Visibility.Visible, "Invalid subgroup name did not keep the picker usable");
            newName.Text = "参考资料"; Invoke(create);
            await WaitFor(() => !picker.IsSaving && App.Favorites.Entries.Any(entry => entry.Name == "参考资料"));
            var newGroup = App.Favorites.Entries.Single(entry => entry.Name == "参考资料");
            Require(newGroup.GroupId == child.Id && picker.SelectedGroupId == newGroup.Id, "New subgroup was not created/selected under its parent");
            results["HierarchicalPickerAndInlineCreation"] = true;
            Invoke(Popup<Button>("FavoriteSave")!);
            await WaitFor(() => Popup<TextBox>("FavoriteName") is null);
            saved = App.Favorites.FindFolders(alpha).Single();
            Require(saved.Name == "My Alpha" && saved.GroupId == newGroup.Id, "Name or group edit was lost");
            AssertStar(star, true);
            results["EditNameAndGroupWithoutDuplication"] = true;

            page.OpenFolderFromUser(beta); await Ready(this, beta); AssertStar(star, false);
            page.ViewModel.Back(); await Ready(this, alpha); AssertStar(star, true);
            page.ViewModel.Forward(); await Ready(this, beta); AssertStar(star, false);
            AddNavigatorTab(alpha); await Ready(this, alpha); AssertStar(star, true);
            Tabs.SelectedItem = originalTab; await Ready(this, beta); AssertStar(star, false);
            Tabs.SelectedItem = Tabs.TabItems[^1]; await Ready(this, alpha); AssertStar(star, true);
            results["NavigationHistoryAndTabSwitches"] = true;

            other = new MainWindow(_pageFactory, new LaunchTarget(null, null), hostTearOut: true);
            App.TrackWindow(other); other.AppWindow.IsShownInSwitchers = false;
            other.AppWindow.Move(new(-10000, -10000)); other.Activate();
            other.AddNavigatorTab(alpha); await Ready(other, alpha); AssertStar(Star(other), true);
            Invoke(star); await WaitFor(() => Popup<Button>("FavoriteRemove") is not null);
            Invoke(Popup<Button>("FavoriteRemove")!);
            await WaitFor(() => App.Favorites.FindFolders(alpha).Count == 0 && Popup<TextBox>("FavoriteName") is null);
            await Task.Delay(100);
            AssertStar(star, false); AssertStar(Star(other), false);
            Require(Directory.Exists(alpha), "Removing a favorite deleted the folder");
            results["RemoveUpdatesBothWindowsWithoutDeletingFolder"] = true;

            await App.Favorites.AddAsync([(alpha, true)], group.Id);
            await Task.Delay(150); AssertStar(star, true);
            Invoke(star); await WaitFor(() => Popup<TextBox>("FavoriteName") is not null);
            Popup<TextBox>("FavoriteName")!.Text = " ";
            Invoke(Popup<Button>("FavoriteSave")!);
            await Task.Delay(200);
            Require(Popup<TextBox>("FavoriteName") is not null && App.Favorites.FindFolders(alpha).Single().Name == "Alpha", "Invalid edit lost the bookmark");
            results["InvalidNamePreservesBookmarkAndEditor"] = true;
            Tabs.SelectedItem = originalTab; await Ready(this, beta);
            await WaitFor(() => Popup<TextBox>("FavoriteName") is null);
            AssertStar(star, false);
            results["NavigationClosesOldFolderEditor"] = true;
            ((NavigatorPage)TabHost.Content).OpenFolderFromUser(HomeLocation.Uri);
            await Ready(this, HomeLocation.Uri);
            Require(!star.IsEnabled, "Home offers a filesystem bookmark");
            results["HomeDisablesStar"] = true;

            var collection = await App.Favorites.CreateGroupAsync("资料归档");
            for (var i = 0; i < 70; i++) await App.Favorites.CreateGroupAsync("项目 " + (i + 1), collection.Id);
            AppWindow.Resize(new(1000, 750));
            await Task.Delay(200);
            var addTask = WindowFavorites.AddPathsAsync([alpha, beta]);
            await WaitFor(() => Popup<FavoriteGroupPicker>("FavoriteGroup") is not null);
            picker = Popup<FavoriteGroupPicker>("FavoriteGroup")!;
            picker.SelectGroup(collection.Id);
            Invoke(FindDescendant<Button>(picker, b => b.Name == "LocationButton")!);
            tree = FindDescendant<TreeView>(picker, _ => true)!;
            await Task.Delay(150);
            var collectionContainer = (TreeViewItem)tree.ContainerFromNode(tree.SelectedNode);
            ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(collectionContainer).GetPattern(PatternInterface.ExpandCollapse)).Expand();
            await Task.Delay(150);
            var addDialog = Popup<ContentDialog>("AddFavoritesDialog")!;
            var dialogSurface = FindDescendant<Border>(addDialog, b => b.Name == "BackgroundElement")!;
            results["LargeTreeMetrics"] = new { Height = tree.ActualHeight, Children = tree.SelectedNode.Children.Count, Expanded = tree.SelectedNode.IsExpanded, Unrealized = tree.SelectedNode.HasUnrealizedChildren };
            await Capture(dialogSurface, "favorite-picker-narrow.png");
            Require(tree.ActualHeight <= 210.5 && tree.SelectedNode.Children.Count == 70, "Large tree is not bounded/complete");
            Require(dialogSurface.ActualWidth <= WindowFavorites.XamlRoot.Size.Width && dialogSurface.ActualHeight <= WindowFavorites.XamlRoot.Size.Height, "Dialog overflows a small window");
            Invoke(FindDescendant<Button>(addDialog, b => b.Name == "PrimaryButton")!);
            await addTask;
            Require(App.Favorites.Entries.Count(entry => entry.GroupId == collection.Id && !entry.IsGroup) == 2, "Batch bookmarks ignored the chosen group");
            results["LargeHierarchyAndNarrowBatchDialog"] = true;
            results["Passed"] = true;
        }
        catch (Exception error) { results["Passed"] = false; results["Error"] = error.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "favorite-star-smoke.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            other?.Close(); Close();
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static Button Star(MainWindow window) => FindDescendant<Button>(window.WindowFavorites,
            b => AutomationProperties.GetAutomationId(b) == "CurrentFolderFavorite")!;
        static void AssertStar(Button star, bool filled) => Require(((FontIcon)star.Content).Glyph == (filled ? "\uE735" : "\uE734"), "Wrong star state");
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        T? Popup<T>(string id) where T : FrameworkElement => VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)Content).XamlRoot)
            .Select(p => p.Child is T root && AutomationProperties.GetAutomationId(root) == id ? root : FindDescendant<T>(p.Child, element => AutomationProperties.GetAutomationId(element) == id)).FirstOrDefault(item => item is not null);
        static async Task WaitFor(Func<bool> ready)
        {
            for (var i = 0; i < 200; i++) { if (ready()) { await Task.Delay(100); return; } await Task.Delay(50); }
            throw new TimeoutException("Favorite UI did not settle");
        }
        static async Task<NavigatorPage> Ready(MainWindow window, string path)
        {
            await WaitFor(() => window.TabHost.Content is NavigatorPage { IsLoaded: true } page
                && !page.ViewModel.IsLoading && page.ViewModel.AddressText == path);
            return (NavigatorPage)window.TabHost.Content;
        }
    }
}
#endif
