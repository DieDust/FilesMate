#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Navigation;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Services;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunTabLinkSmokeAsync()
    {
        var results = new Dictionary<string, object>();
        MainWindow? other = null;
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1280, 850));
            var fixture = Path.Combine(AppContext.BaseDirectory, "tab-link-fixture");
            var folderA = Path.Combine(fixture, "Alpha"); var folderB = Path.Combine(fixture, "Beta");
            Directory.CreateDirectory(folderA); Directory.CreateDirectory(folderB);
            File.WriteAllText(Path.Combine(folderA, "report.txt"), "fixture");
            AddNavigatorTab(folderA); var pageA = await Ready(this, folderA);
            var tabA = (TabViewItem)Tabs.SelectedItem;
            await App.Favorites.SaveFolderAsync(folderB);
            App.SetFavoritesBarEnabled(true);
            await Task.Delay(600);
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { OpenFoldersInNewTab = true });
            var favorite = FindDescendant<Button>(WindowFavorites, b => AutomationProperties.GetName(b) == "Beta")
                ?? throw new InvalidOperationException("Favorite button not rendered");
            var count = Tabs.TabItems.Count;
            Invoke(favorite);
            var pageB = await Ready(this, folderB);
            Require(Tabs.TabItems.Count == count + 1 && pageA.ViewModel.AddressText == folderA, "Favorite replaced the original tab");
            Require(pageB.ViewModel.Navigation.CanGoBack, "New tab lost its origin history");
            results["FavoriteNewTabAndHistory"] = true;
            var tabB = (TabViewItem)Tabs.SelectedItem;
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { OpenFoldersInNewTab = false });
            pageB.OpenFolderFromUser(folderA); await Ready(this, folderA);
            count = Tabs.TabItems.Count; Invoke(favorite); await Ready(this, folderB);
            Require(Tabs.TabItems.Count == count && ReferenceEquals(tabB, Tabs.SelectedItem), "Current-tab preference ignored");
            results["FavoriteCurrentTab"] = true;

            AppWindow.Resize(new(1250, 650));
            await Task.Delay(400);
            var header = FindDescendant<Grid>(pageB, item => item.Name == "DetailsHeader")!;
            var glyph = FindDescendant<FontIcon>(pageB, item => item.Name == "GlyphIcon")!;
            var headerBottom = header.TransformToVisual(pageB).TransformBounds(new Rect(0, 0, header.ActualWidth, header.ActualHeight)).Bottom;
            var glyphTop = glyph.TransformToVisual(pageB).TransformPoint(new Point()).Y;
            Require(glyphTop >= headerBottom + 4, "Empty-state icon overlaps column headers in a small window");
            results["EmptyStateBelowHeaders"] = new { headerBottom, glyphTop };
            await Capture((UIElement)Content, "tab-link-empty-small.png");
            var surface = FindDescendant<FileDetailsSurface>(pageB, _ => true)!;
            var chrome = FindDescendant<FilePaneChrome>(pageB, _ => true)!;
            surface.SetLayout(FileLayoutKind.Grid);
            await Task.Delay(150);
            Require(chrome.ContentTopInset == 0 && header.Visibility == Visibility.Collapsed, "Icon view retained a column-header inset");
            surface.SetLayout(FileLayoutKind.Details);
            await Task.Delay(150);
            Require(chrome.ContentTopInset > 0 && header.Visibility == Visibility.Visible, "Details view lost its header inset");
            results["EmptyStateFollowsViewMode"] = true;
            AppWindow.Resize(new(1280, 850));
            await Task.Delay(400);
            favorite = FindDescendant<Button>(WindowFavorites, b => AutomationProperties.GetName(b) == "Beta")!;

            var unloads = 0;
            WindowFavorites.Unloaded += (_, _) => unloads++;
            var image = FindDescendant<ImageIcon>(favorite, _ => true);
            var icon = image?.Source;
            var originalMargin = WindowFavorites.Margin;
            for (var i = 0; i < 12; i++)
            {
                Tabs.SelectedItem = i % 2 == 0 ? tabA : tabB;
                await Ready(this, i % 2 == 0 ? folderA : folderB);
                Require(ReferenceEquals(favorite, FindDescendant<Button>(WindowFavorites, b => AutomationProperties.GetName(b) == "Beta")), "Favorites were rebuilt");
                Require(ReferenceEquals(icon, image?.Source), "Favorite icon was replaced");
                Require(WindowFavorites.Margin == originalMargin, "Favorites bar moved");
            }
            Require(unloads == 0, "Favorites bar unloaded during tab switches");
            results["StableBarAcross12Switches"] = new { unloads, SameButtonsAndIcons = true };
            await Capture(WindowFavorites, "tab-link-favorites.png");

            other = new MainWindow(_pageFactory, new LaunchTarget(null, null), hostTearOut: true);
            App.TrackWindow(other); other.AppWindow.IsShownInSwitchers = false;
            other.AppWindow.Move(new(-10000, -10000)); other.Activate();
            other.AddNavigatorTab(folderB); await Ready(other, folderB);
            var live = ((NavigatorTabContent)tabA.Tag).Navigator;
            other.MoveTabFrom(this, tabA, other.Tabs.TabItems.Count);
            await Ready(other, folderA);
            Require(!Tabs.TabItems.Contains(tabA) && ReferenceEquals(live, ((NavigatorTabContent)tabA.Tag).Navigator), "Live tab did not transfer");
            Require(App.WindowForElement(live!) == other, "Transferred page has the wrong owner");
            results["MergeAtTabStripEnd"] = true;
            MoveTabFrom(other, tabA, 0); await Ready(this, folderA);
            Require(Tabs.TabItems.IndexOf(tabA) == 0, "Drop insertion position lost");
            results["MergeAtInsertionPosition"] = true;
            // An internal reorder reports Move too; it must never close a tab.
            count = Tabs.TabItems.Count; _handledTabDrop = false;
            CompleteTabTransfer(tabA, DataPackageOperation.Move);
            Require(Tabs.TabItems.Count == count, "Reorder incorrectly removed its source");
            results["LocalReorderKeepsTab"] = true;

            var snapshot = CreateTabTransfer(tabA) with { SourceProcessId = int.MaxValue };
            Require(other.ReceiveExternalTab(TabTransferPayload.Parse(snapshot.Serialize())!, 0), "Serialized tab was rejected");
            var restored = await Ready(other, folderA);
            Require(restored.CaptureClosedTab().Left.Path == folderA, "Serialized tab restored wrong directory");
            Require(!other.ReceiveExternalTab(snapshot, 0), "Duplicate delivery inserted a second tab");
            results["SerializedTabRestoredAndDeduplicated"] = true;
            _tabTransferReceipt = new EventWaitHandle(false, EventResetMode.ManualReset, snapshot.ReceiptName);
            CompleteTabTransfer(tabA, DataPackageOperation.Move);
            Require(Tabs.TabItems.Contains(tabA), "Source removed before acknowledgement");
            using (var receipt = EventWaitHandle.OpenExisting(snapshot.ReceiptName)) receipt.Set();
            CompleteTabTransfer(tabA, DataPackageOperation.None);
            Require(Tabs.TabItems.Contains(tabA), "Canceled drag removed the source");
            CompleteTabTransfer(tabA, DataPackageOperation.Move);
            Require(!Tabs.TabItems.Contains(tabA), "Acknowledged move retained the source");
            _tabTransferReceipt.Dispose(); _tabTransferReceipt = null;
            results["ReceiptAndMoveRequiredToRemoveSource"] = true;
            foreach (var window in new[] { this, other })
            {
                window.UpdateNonClientRegions();
                var first = (TabViewItem)window.Tabs.TabItems[0];
                var bounds = first.TransformToVisual(window.Tabs).TransformBounds(new Rect(0, 0, first.ActualWidth, first.ActualHeight));
                Require(window.TryGetTabInsertion(new Point(bounds.Left + 8, bounds.Top + bounds.Height / 2), out var before, out _) && before == 0,
                    "Left half did not insert before the tab");
                Require(window.TryGetTabInsertion(new Point(bounds.Right - 8, bounds.Top + bounds.Height / 2), out var after, out _) && after == 1,
                    "Right half did not insert after the tab");
                var blank = window.TabDragRegion.TransformToVisual(window.Tabs).TransformPoint(new Point(20, 10));
                Require(!window.TryGetTabInsertion(blank, out _, out _), "Blank caption incorrectly accepts tab merges");
                var addButton = window.NewTabButton.TransformToVisual(window.Tabs).TransformPoint(new Point(15, 15));
                Require(!window.TryGetTabInsertion(addButton, out _, out _), "New-tab button incorrectly accepts tab merges");
            }
            results["OnlyExistingTabEdgesAcceptInsertion"] = true;
            results["Passed"] = true;
        }
        catch (Exception error) { results["Passed"] = false; results["Error"] = error.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tab-link-smoke.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            other?.Close(); Close();
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        static async Task<NavigatorPage> Ready(MainWindow window, string path)
        {
            for (var i = 0; i < 200; i++)
            {
                if (window.Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { RestoreState: null, Navigator: { IsLoaded: true } page } }
                    && !page.ViewModel.IsLoading && page.ViewModel.AddressText == path)
                { await Task.Delay(100); return page; }
                await Task.Delay(50);
            }
            throw new TimeoutException("Tab not ready: " + path);
        }
    }
}
#endif
