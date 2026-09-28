using FilesMate.App.Views;
using FilesMate.Search;
using Microsoft.UI.Xaml.Controls;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    public void OpenSearchPage(SearchPageRequest request)
    {
        CloseSettings();
        NavigationSidebar.SelectPath(request.Scope ?? "");
        var page = new SearchResultsPage(request);
        var item = new TabViewItem { Tag = page, IconSource = TabIcon("\uE721") };
        void UpdateCaption() => ApplyHeader(item, string.IsNullOrWhiteSpace(page.Request.Query) ? Loc.Get("SearchPage_Title") : Loc.Get("SearchPage_Title") + " · " + page.Request.Query);
        page.RequestChanged += (_, _) => UpdateCaption();
        ApplyTabItemStyle(item); UpdateCaption();
        Tabs.TabItems.Add(item); PaintTabHeaders(); Tabs.SelectedItem = item;
        RefreshClosable(); ShowSelectedPage();
    }

    public void OpenSearchResultLocation(SearchResultsPage source, string path, string? select = null)
    {
        var previous = Tabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => ReferenceEquals(t.Tag, source));
        AddNavigatorTab(path, select);
        if (!App.ExplorerPreferences.OpenFoldersInNewTab && previous is not null) CloseTab(previous);
    }
}
