using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Theming;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class AdaptiveCommandToolbar
{
    private FileLayoutKind _viewKind;
    private GridSizePreset _viewSize = GridSizePreset.Default;
    private int _listZoom = 100;
    private readonly MenuFlyoutItem _overflowSortPanel = new();
    private Action? _syncSortPanel, _syncGroupingPanel;
    public event EventHandler<EntrySort>? SortSpecificationRequested;
    public event EventHandler<GridSizePreset>? GridSizeRequested;
    public event EventHandler<int>? ListZoomRequested;
    public void SetViewSize(GridSizePreset size, int listZoom) { _viewSize = size; _listZoom = listZoom; SetLayout(_viewKind); }

    private static TextBlock PanelCaption(string key) => new()
    { Text = StringTable.Get(key), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
    private static StackPanel PanelBody(double width) => new() { Width = width, Padding = new Thickness(16), Spacing = 8 };
    private static Flyout PanelFlyout(UIElement content)
    {
        var flyout = new Flyout { Content = content, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        FlyoutTheme.FollowHost(flyout);
        return flyout;
    }

    private void InitializeViewControls()
    {
        SortButton.Flyout = CreateSortPanel();
        _overflowSortPanel.Text = StringTable.Get("Sort");
        _overflowSortPanel.Click += (_, _) => DispatcherQueue.TryEnqueue(() => SortButton.Flyout.ShowAt(MoreButton));
        ((MenuFlyout)MoreButton.Flyout).Items.Insert(0, _overflowSortPanel);
        GroupingMenuButton.Flyout = CreateGroupingPanel();
        Caption(GroupingMenuButton, "Grouping_Options");
        Caption(ViewMenuButton, "View_Options");
        var views = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var choices = new List<(ToggleMenuFlyoutItem Item, FileLayoutKind Kind, int Slot)>();
        ToggleMenuFlyoutItem Add(string key, FileLayoutKind kind, GridSizePreset? preset = null)
        {
            var item = new ToggleMenuFlyoutItem { Text = StringTable.Get(key) };
            item.Click += (_, _) =>
            {
                if (preset is { } size) GridSizeRequested?.Invoke(this, size);
                LayoutChanged?.Invoke(this, kind);
            };
            choices.Add((item, kind, preset?.Slot ?? 0));
            return item;
        }
        views.Items.Add(Add("Layout_List", FileLayoutKind.List));
        views.Items.Add(Add("Layout_LargeIcons", FileLayoutKind.Grid, GridSizePreset.Large));
        views.Items.Add(Add("Layout_Details", FileLayoutKind.Details));
        views.Items.Add(new MenuFlyoutSeparator());
        var iconSizes = new MenuFlyoutSubItem { Text = StringTable.Get("View_IconSize") };
        foreach (var preset in GridSizePreset.All.Reverse()) iconSizes.Items.Add(Add(preset.ZoomKey, FileLayoutKind.Grid, preset));
        views.Items.Add(iconSizes);
        var listZoom = new MenuFlyoutSubItem { Text = StringTable.Get("View_ListZoom") };
        var zooms = new List<ToggleMenuFlyoutItem>();
        foreach (var value in new[] { 80, 100, 120, 140, 160 })
        {
            var item = new ToggleMenuFlyoutItem { Text = value + "%", Tag = value };
            item.Click += (_, _) => { LayoutChanged?.Invoke(this, FileLayoutKind.List); ListZoomRequested?.Invoke(this, value); };
            listZoom.Items.Add(item); zooms.Add(item);
        }
        views.Items.Add(listZoom);
        views.Opening += (_, _) =>
        {
            foreach (var choice in choices) choice.Item.IsChecked = choice.Kind == _viewKind && (choice.Kind != FileLayoutKind.Grid || choice.Slot == _viewSize.Slot);
            foreach (var zoom in zooms) zoom.IsChecked = (int)zoom.Tag == _listZoom;
        };
        FlyoutTheme.FollowHost(views);
        ViewMenuButton.Flyout = views;
        SyncGroupingIcon();
    }

    private Flyout CreateSortPanel()
    {
        var body = PanelBody(304);
        body.Children.Add(PanelCaption("Sort"));
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        var fields = new List<(DetailsColumn Column, RadioButton Button)>();
        var syncing = false;
        foreach (var column in DetailsColumn.Defaults().Where(c => c.CanSort))
        {
            var index = fields.Count;
            if (index % 2 == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var radio = new RadioButton { Content = column.Title, GroupName = "Sort" + GetHashCode(), FontSize = 13, MinHeight = 32, Tag = column.Sort };
            Grid.SetRow(radio, index / 2); Grid.SetColumn(radio, index % 2);
            radio.Checked += (_, _) =>
            {
                if (syncing) return;
                _sort = _sort with { Column = column.Sort, PropertyName = column.PropertyName };
                SortSpecificationRequested?.Invoke(this, _sort);
            };
            fields.Add((column, radio)); grid.Children.Add(radio);
        }
        body.Children.Add(grid);
        var directions = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        directions.ColumnDefinitions.Add(new()); directions.ColumnDefinitions.Add(new());
        var asc = new Button { Content = StringTable.Get("Sort_Ascending"), HorizontalAlignment = HorizontalAlignment.Stretch, Height = 32, FontSize = 13 };
        var desc = new Button { Content = StringTable.Get("Sort_Descending"), HorizontalAlignment = HorizontalAlignment.Stretch, Height = 32, FontSize = 13 };
        Grid.SetColumn(desc, 1); directions.Children.Add(asc); directions.Children.Add(desc); body.Children.Add(directions);
        void Sync()
        {
            syncing = true;
            foreach (var item in fields) item.Button.IsChecked = item.Column.IsSortedBy(_sort);
            SetActive(asc, _sort.Ascending); SetActive(desc, !_sort.Ascending);
            syncing = false;
        }
        void Direction(bool ascending)
        {
            _sort = _sort with { Ascending = ascending };
            SortSpecificationRequested?.Invoke(this, _sort); Sync();
        }
        asc.Click += (_, _) => Direction(true); desc.Click += (_, _) => Direction(false);
        _syncSortPanel = Sync;
        var flyout = PanelFlyout(body); flyout.Opening += (_, _) => Sync(); return flyout;
    }

    private Flyout CreateGroupingPanel()
    {
        var body = PanelBody(double.NaN);
        body.MinWidth = 196;
        body.MaxWidth = 300;
        body.Children.Add(PanelCaption("Grouping_Options"));
        var modes = new[] { EntryGrouping.FoldersFirst, EntryGrouping.Mixed, EntryGrouping.FilesFirst };
        var radios = new List<RadioButton>(); var checks = new List<CheckBox>();
        var syncing = false;
        foreach (var mode in modes)
        {
            var icon = new EntryGroupingIcon(); icon.SetGrouping(mode);
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            label.Children.Add(icon);
            label.Children.Add(new TextBlock { Text = StringTable.Get("Sort_" + mode), VerticalAlignment = VerticalAlignment.Center });
            var radio = new RadioButton { Content = label, Tag = mode, GroupName = "Grouping" + GetHashCode(), MinHeight = 32, FontSize = 13 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(radio, StringTable.Get("Sort_" + mode));
            radio.Checked += (_, _) => { if (!syncing) ChangeGrouping(mode); };
            body.Children.Add(radio); radios.Add(radio);
        }
        var heading = PanelCaption("Grouping_Cycle"); heading.Margin = new Thickness(0, 12, 0, 0);
        heading.TextWrapping = TextWrapping.Wrap; body.Children.Add(heading);
        foreach (var mode in modes)
        {
            var check = new CheckBox { Content = StringTable.Get("Sort_" + mode), Tag = mode, MinHeight = 30, FontSize = 13 };
            async void Save(object sender, RoutedEventArgs e)
            {
                if (syncing) return;
                var cycle = checks.Where(c => c.IsChecked == true).Select(c => (EntryGrouping)c.Tag).ToArray();
                if (cycle.Length == 0) { Sync(); return; }
                try { await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { GroupingClickCycle = cycle }); }
                catch (Exception error) { App.LogFailure("GroupingCycle", error); }
                Sync();
            }
            check.Checked += Save; check.Unchecked += Save;
            body.Children.Add(check); checks.Add(check);
        }
        void Sync()
        {
            syncing = true;
            foreach (var radio in radios) radio.IsChecked = (EntryGrouping)radio.Tag == _sort.EffectiveGrouping;
            var cycle = App.ExplorerPreferences.EffectiveGroupingCycle;
            foreach (var check in checks)
            {
                check.IsChecked = cycle.Contains((EntryGrouping)check.Tag);
                check.IsEnabled = cycle.Count > 1 || check.IsChecked != true;
            }
            syncing = false;
        }
        _syncGroupingPanel = Sync;
        var flyout = PanelFlyout(body); flyout.Opening += (_, _) => Sync(); return flyout;
    }
    private void GroupingButton_Click(object sender, RoutedEventArgs e) => ChangeGrouping(App.ExplorerPreferences.NextGrouping(_sort.EffectiveGrouping));
    private void ChangeGrouping(EntryGrouping grouping)
    {
        _sort = _sort with { Grouping = grouping, DirectoriesFirst = grouping == EntryGrouping.FoldersFirst };
        GroupingRequested?.Invoke(this, grouping); SyncGroupingIcon();
    }
    private void SyncGroupingIcon()
    {
        GroupingIcon.SetGrouping(_sort.EffectiveGrouping);
        Caption(GroupingButton, "Sort_" + _sort.EffectiveGrouping);
        _syncSortPanel?.Invoke(); _syncGroupingPanel?.Invoke();
    }
}
