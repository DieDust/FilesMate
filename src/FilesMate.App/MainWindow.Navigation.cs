using FilesMate.App.Controls.Navigation;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    internal NavigationSidebar NavigationSidebar => WindowNavigation.Sidebar;
    internal void ToggleNavigation() => WindowNavigation.Toggle();
    internal void PaintNavigationToggle(Button button, FontIcon icon)
    {
        icon.Glyph = WindowNavigation.IsOpen ? "\uE76B" : "\uE76C";
        var text = Loc.Get(WindowNavigation.IsOpen ? "Sidebar_Collapse" : "Sidebar_Expand");
        AutomationProperties.SetName(button, text); ToolTipService.SetToolTip(button, text);
    }
    private void InitializeNavigation()
    {
        NavigationSidebar.PlaceChosen += (_, path) =>
        {
            if (TabHost.Content is NavigatorPage page) page.OpenFolderFromUser(path);
            else if (TabHost.Content is SearchResultsPage search) OpenSearchResultLocation(search, path);
            else OpenFolderInNewTab(path);
        };
        NavigationSidebar.OpenInNewTabRequested += (_, path) => OpenFolderInNewTab(path);
        NavigationSidebar.OpenInNewWindowRequested += (_, path) => OpenFolderInNewWindow(path);
        NavigationSidebar.SettingsClicked += (_, _) => OpenSettings();
        NavigationSidebar.WhoLocksRequested += (_, path) =>
        {
            if (TabHost.Content is NavigatorPage page) page.Sidebar_WhoLocksRequested(null, path);
            else if (TabHost.Content is SearchResultsPage search) search.ShowWhoLocks(path);
        };
        NavigationSidebar.PinnedLocationsChanged += (_, _) =>
        { if (TabHost.Content is NavigatorPage page) page.Sidebar_PinnedLocationsChanged(null, EventArgs.Empty); };
        WindowNavigation.StateChanged += (_, _) =>
        {
            if (TabHost.Content is NavigatorPage page) page.RefreshNavigationToggle();
            if (TabHost.Content is SearchResultsPage search) search.RefreshNavigationToggle();
        };
    }
}
