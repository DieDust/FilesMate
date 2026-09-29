using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar
{
    private bool Hidden(ToolbarTool tool) => App.ExplorerPreferences.HiddenToolbarTools?.Contains(tool) == true;

    private IEnumerable<(ToolbarTool Tool, Button Button, string Label)> CustomizableTools()
    {
        yield return (ToolbarTool.New, NewButton, "Command_New");
        yield return (ToolbarTool.Cut, CutButton, "Command_Cut");
        yield return (ToolbarTool.Copy, CopyButton, "Command_Copy");
        yield return (ToolbarTool.Paste, PasteButton, "Command_Paste");
        yield return (ToolbarTool.Rename, RenameButton, "Command_Rename");
        yield return (ToolbarTool.Share, ShareButton, "Command_Share");
        yield return (ToolbarTool.Delete, DeleteButton, "Command_Recycle");
        yield return (ToolbarTool.CopyPath, CopyPathButton, "Command_CopyPath");
        yield return (ToolbarTool.HiddenFiles, HiddenFilesButton, "ShowHiddenFilesTitle");
        yield return (ToolbarTool.Shelf, ShelfButton, "Shelf_Title");
        yield return (ToolbarTool.Sort, SortButton, "Sort");
        yield return (ToolbarTool.Grouping, GroupingButton, "Grouping_Options");
        yield return (ToolbarTool.FolderSizes, FolderSizesButton, "ShowFolderSizesTitle");
        yield return (ToolbarTool.ViewMenu, ViewMenuButton, "View_Options");
        yield return (ToolbarTool.SplitView, DualPaneButton, "DualPane");
        yield return (ToolbarTool.Preview, PreviewButton, "PreviewPaneTitle");
    }

    private void ApplyHiddenTools()
    {
        foreach (var (tool, button, _) in CustomizableTools())
            if (Hidden(tool)) button.Visibility = Visibility.Collapsed;
        if (Hidden(ToolbarTool.SplitView)) PaneArrangementButton.Visibility = Visibility.Collapsed;
        GroupingGroup.Visibility = Hidden(ToolbarTool.Grouping) ? Visibility.Collapsed : Visibility.Visible;
        ViewSeparator.Visibility = AnyVisible(ShelfButton, SortButton, FolderSizesButton)
            && AnyVisible(ViewMenuButton, MoreButton, DualPaneButton, PreviewButton)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private Flyout CreateCustomizationMenu()
    {
        var body = PanelBody(344);
        body.Children.Add(PanelCaption("Toolbar_Customize"));
        var checks = new List<(ToolbarTool Tool, CheckBox Check)>();
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        var syncing = false;
        foreach (var (tool, _, label) in CustomizableTools())
        {
            if (SearchMode && tool is ToolbarTool.New or ToolbarTool.HiddenFiles or ToolbarTool.Sort or ToolbarTool.FolderSizes or ToolbarTool.SplitView) continue;
            var index = checks.Count;
            if (index % 2 == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var item = new CheckBox { Content = StringTable.Get(label), IsChecked = !Hidden(tool), Tag = tool, FontSize = 13, MinHeight = 34 };
            async void Changed(object sender, RoutedEventArgs e)
            {
                if (syncing) return;
                var hidden = (App.ExplorerPreferences.HiddenToolbarTools ?? []).ToHashSet();
                if (item.IsChecked == true) hidden.Remove(tool); else hidden.Add(tool);
                await SaveToolbarToolsAsync(hidden.ToArray());
            }
            item.Checked += Changed; item.Unchecked += Changed;
            Grid.SetRow(item, index / 2); Grid.SetColumn(item, index % 2);
            grid.Children.Add(item); checks.Add((tool, item));
        }
        body.Children.Add(grid);
        var reset = new Button { Content = StringTable.Get("Toolbar_Restore"), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, MinHeight = 32 };
        reset.Click += async (_, _) =>
        {
            await SaveToolbarToolsAsync(null);
            syncing = true;
            foreach (var (tool, check) in checks) check.IsChecked = !Hidden(tool);
            syncing = false;
        };
        body.Children.Add(reset);
        return PanelFlyout(body);
    }

    private async Task SaveToolbarToolsAsync(IReadOnlyList<ToolbarTool>? hidden)
    {
        try { await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { HiddenToolbarTools = hidden }); }
        catch (Exception error) { App.LogFailure("ToolbarCustomization", error); }
        ApplyContext(_context);
    }

    private void Toolbar_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        CreateCustomizationMenu().ShowAt(this, new FlyoutShowOptions
        { Position = e.GetPosition(this), Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }
}
