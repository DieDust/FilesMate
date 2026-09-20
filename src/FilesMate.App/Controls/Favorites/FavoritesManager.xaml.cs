using Loc = FilesMate.App.Localization.StringTable;
using System.Collections.ObjectModel;
using System.Text.Json;
using FilesMate.App.Icons;
using FilesMate.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace FilesMate.App.Controls.Favorites;

public sealed partial class FavoritesManager : UserControl
{
    private const string BatchDragFormat = "FilesMate.FavoriteIds";
    private readonly ObservableCollection<EntryRow> _rows = [];
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.Ordinal);
    private string? _groupId;
    private string? _editingId;
    private bool _refreshing;
    private bool _busy;
    private bool _confirmRemoval;
    private string[] _dragOrder = [];
    public event EventHandler<FavoriteEntry>? OpenRequested;
    public event EventHandler? CloseRequested;

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void Manager_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || e.Handled) return;
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public sealed record GroupRow(string? Id, string Name, int Count);
    public sealed record EntryRow(FavoriteEntry Entry, string Detail, ImageSource Icon)
    {
        public string Name => Entry.Name;
    }

    public FavoritesManager(string? groupId)
    {
        InitializeComponent();
        InitializeBoxSelection();
        _groupId = groupId;
        Entries.ItemsSource = _rows;
        Loaded += (_, _) => { App.Favorites.Changed += StoreChanged; Refresh(); };
        Unloaded += (_, _) => App.Favorites.Changed -= StoreChanged;
        SizeChanged += (_, _) =>
        {
            GroupsColumn.Width = new GridLength(ActualWidth < 650 ? 140 : 190);
            Layout.ColumnSpacing = ActualWidth < 650 ? 10 : 20;
        };
    }

    private void StoreChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => { if (IsLoaded) Refresh(); });

    private void EntryIcon_Loaded(object sender, RoutedEventArgs e) => BindEntryIcon((Image)sender);
    private void EntryIcon_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => BindEntryIcon((Image)sender);
    private static void BindEntryIcon(Image image)
    {
        var fallback = new FontIcon();
        ShellIconBinder.Clear(image, fallback);
        if (image.DataContext is not EntryRow row) return;
        if (row.Entry.IsGroup)
        {
            image.Source = row.Icon;
            image.Visibility = Visibility.Visible;
        }
        else ShellIconBinder.BindPath(image, fallback, row.Entry.Path!, row.Entry.IsDirectory, 28);
    }

    private void Refresh()
    {
        if (_refreshing) return;
        EndBoxSelection();
        _refreshing = true;
        try
        {
            var selected = Entries.SelectedItems.OfType<EntryRow>().Select(row => row.Entry.Id).ToHashSet();
            var all = App.Favorites.Entries;
            if (_groupId is not null && !all.Any(entry => entry.IsGroup && entry.Id == _groupId)) _groupId = null;
            var counts = all.Where(entry => entry.GroupId is not null).GroupBy(entry => entry.GroupId!).ToDictionary(group => group.Key, group => group.Count());
            var groups = new[] { new GroupRow(null, Loc.Get("Favorites_Bar"), all.Count(entry => entry.GroupId is null)) }
                .Concat(all.Where(entry => entry.IsGroup).Select(entry => new GroupRow(entry.Id, entry.Name, counts.GetValueOrDefault(entry.Id)))).ToArray();
            Groups.ItemsSource = groups;
            Groups.SelectedItem = groups.First(group => group.Id == _groupId);
            var keyword = Search.Text.Trim();
            _rows.Clear();
            foreach (var entry in all.Where(entry => entry.GroupId == _groupId && (keyword.Length == 0 || entry.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) || (entry.Path?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))))
            {
                var uri = entry.IsGroup ? FavoritesBar.GroupIconUri : FileTypeIconCatalog.AssetUri(FileTypeIconCatalog.ClassifyPath(entry.Path!, entry.IsDirectory) ?? FileIconKind.Document);
                if (!_icons.TryGetValue(uri, out var icon)) _icons[uri] = icon = new SvgImageSource(new Uri(uri));
                var row = new EntryRow(entry, entry.IsGroup ? Loc.Format("Favorites_Count", counts.GetValueOrDefault(entry.Id)) : entry.Path!, icon);
                _rows.Add(row);
                if (selected.Contains(entry.Id)) Entries.SelectedItems.Add(row);
            }
            Entries.CanReorderItems = keyword.Length == 0;
            Empty.Text = keyword.Length == 0 ? Loc.Get("Favorites_GroupEmpty") : Loc.Get("Favorites_NoMatch");
            Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _refreshing = false; }
        UpdateSelection();
    }

    private string[] SelectedIds() => Entries.SelectedItems.OfType<EntryRow>().Select(row => row.Entry.Id).ToArray();
    private void UpdateSelection()
    {
        _confirmRemoval = false;
        RemoveButton.Content = Loc.Get("Remove");
        var count = Entries.SelectedItems.Count;
        SelectionCount.Text = $"{count} / {_rows.Count}";
        MoveButton.IsEnabled = RemoveButton.IsEnabled = count > 0 && !_busy;
    }
    private void Entries_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_refreshing) UpdateSelection(); }
    private void Groups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Groups.SelectedItem is not GroupRow group) return;
        _groupId = group.Id;
        Editor.Visibility = Visibility.Collapsed;
        Refresh();
    }
    private void Search_TextChanged(object sender, TextChangedEventArgs e) { if (Entries is not null) Refresh(); }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItems.Count == _rows.Count) Entries.SelectedItems.Clear(); else Entries.SelectAll();
    }

    private async Task ChangeAsync(Func<Task> change)
    {
        if (_busy) return;
        _busy = true;
        try { await change(); Status.Text = Loc.Get("Saved"); }
        catch (Exception error) { Status.Text = error.Message; }
        finally { _busy = false; Refresh(); }
    }

    private void ShowMoveMenu(FrameworkElement anchor, string[] ids)
    {
        var menu = new MenuFlyout();
        var includesGroup = App.Favorites.Entries.Any(entry => entry.IsGroup && ids.Contains(entry.Id));
        void Add(string name, string? group)
        {
            var item = new MenuFlyoutItem { Text = name, IsEnabled = !includesGroup || group is null };
            item.Click += async (_, _) => await ChangeAsync(() => App.Favorites.MoveManyAsync(ids, group));
            menu.Items.Add(item);
        }
        Add(Loc.Get("Favorites_Bar"), null);
        foreach (var group in App.Favorites.Entries.Where(entry => entry.IsGroup)) Add(group.Name, group.Id);
        menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }
    private void Move_Click(object sender, RoutedEventArgs e) => ShowMoveMenu(MoveButton, SelectedIds());
    private async void Remove_Click(object sender, RoutedEventArgs e) => await RemoveSelectedAsync();
    private async Task RemoveSelectedAsync()
    {
        var ids = SelectedIds();
        if (ids.Length == 0) return;
        if (!_confirmRemoval && App.Favorites.Entries.Any(entry => entry.GroupId is { } parent && ids.Contains(parent)))
        {
            _confirmRemoval = true;
            RemoveButton.Content = Loc.Get("ConfirmRemove");
            Status.Text = Loc.Get("Favorites_RemoveHint");
            return;
        }
        await ChangeAsync(() => App.Favorites.RemoveManyAsync(ids));
    }

    private void BeginNameEdit(FavoriteEntry? entry)
    {
        _editingId = entry?.Id;
        NameEditor.Text = entry?.Name ?? "";
        NameEditor.PlaceholderText = entry is null ? Loc.Get("NewGroupName") : Loc.Get("Favorites_Name");
        Editor.Visibility = Visibility.Visible;
        NameEditor.Focus(FocusState.Programmatic);
        NameEditor.SelectAll();
    }
    private void NewGroup_Click(object sender, RoutedEventArgs e) => BeginNameEdit(null);
    private void CancelName_Click(object sender, RoutedEventArgs e) => Editor.Visibility = Visibility.Collapsed;
    private async void SaveName_Click(object sender, RoutedEventArgs e) => await SaveNameAsync();
    private async Task SaveNameAsync()
    {
        var name = NameEditor.Text;
        var id = _editingId;
        await ChangeAsync(async () =>
        {
            if (id is null) await App.Favorites.CreateGroupAsync(name); else await App.Favorites.RenameAsync(id, name);
            Editor.Visibility = Visibility.Collapsed;
        });
    }
    private async void NameEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await SaveNameAsync(); }
        else if (e.Key == VirtualKey.Escape) { e.Handled = true; Editor.Visibility = Visibility.Collapsed; }
    }

    private void Entries_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var container = args.ItemContainer;
        container.ContextRequested -= Entry_ContextRequested;
        container.Tag = args.InRecycleQueue ? null : args.Item;
        AutomationProperties.SetName(container, args.Item is EntryRow row && !args.InRecycleQueue ? row.Name : "");
        if (!args.InRecycleQueue) container.ContextRequested += Entry_ContextRequested;
    }
    private void Entry_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListViewItem { Tag: EntryRow row } container) return;
        e.Handled = true;
        if (!Entries.SelectedItems.Contains(row)) { Entries.SelectedItems.Clear(); Entries.SelectedItems.Add(row); }
        var menu = new MenuFlyout();
        void Add(string label, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add(row.Entry.IsGroup ? Loc.Get("OpenGroup") : Loc.Get("Command_Open"), () => Open(row.Entry), Entries.SelectedItems.Count == 1);
        Add(Loc.Get("RenameMenu"), () => BeginNameEdit(row.Entry), Entries.SelectedItems.Count == 1);
        Add(Loc.Get("MoveToMenu"), () => ShowMoveMenu(MoveButton, SelectedIds()));
        Add(Loc.Get("Home_MoveUp"), async () => await ChangeAsync(() => App.Favorites.ReorderAsync(row.Entry.Id, -1)), Entries.SelectedItems.Count == 1);
        Add(Loc.Get("Home_MoveDown"), async () => await ChangeAsync(() => App.Favorites.ReorderAsync(row.Entry.Id, 1)), Entries.SelectedItems.Count == 1);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(Loc.Get("Favorites_Remove"), async () => await RemoveSelectedAsync());
        var options = new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        if (e.TryGetPosition(container, out var point)) options.Position = point;
        menu.ShowAt(container, options);
    }
    private void Open(FavoriteEntry entry)
    {
        if (entry.IsGroup) { _groupId = entry.Id; Search.Text = ""; Refresh(); }
        else OpenRequested?.Invoke(this, entry);
    }
    private void Entries_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != Entries; element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem { Tag: EntryRow row })
            { e.Handled = true; Open(row.Entry); break; }
    }
    private async void Entries_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.A && IsSelectionModifier(VirtualKey.Control)) { Entries.SelectAll(); e.Handled = true; return; }
        if (e.Key == VirtualKey.Delete) { e.Handled = true; await RemoveSelectedAsync(); }
        if (Entries.SelectedItems.Count != 1 || Entries.SelectedItems[0] is not EntryRow row) return;
        if (e.Key == VirtualKey.F2) { e.Handled = true; BeginNameEdit(row.Entry); }
        else if (e.Key == VirtualKey.Enter) { e.Handled = true; Open(row.Entry); }
    }

    private void Entries_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragOrder = _rows.Select(row => row.Entry.Id).ToArray();
        e.Data.SetData(BatchDragFormat, JsonSerializer.Serialize(e.Items.OfType<EntryRow>().Select(row => row.Entry.Id)));
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }
    private async void Entries_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var order = _rows.Select(row => row.Entry.Id).ToArray();
        if (Entries.CanReorderItems && args.DropResult == DataPackageOperation.Move && !_dragOrder.SequenceEqual(order))
            await ChangeAsync(() => App.Favorites.SetOrderAsync(_groupId, order));
    }
    private void Groups_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var container = args.ItemContainer;
        container.DragOver -= Group_DragOver;
        container.Drop -= Group_Drop;
        container.ContextRequested -= Group_ContextRequested;
        container.Tag = args.InRecycleQueue ? null : args.Item;
        AutomationProperties.SetName(container, args.Item is GroupRow row && !args.InRecycleQueue ? row.Name : "");
        container.AllowDrop = !args.InRecycleQueue;
        if (args.InRecycleQueue) return;
        container.DragOver += Group_DragOver;
        container.Drop += Group_Drop;
        container.ContextRequested += Group_ContextRequested;
    }
    private void Group_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(BatchDragFormat)) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = Loc.Get("Favorites_MoveIntoGroup");
        e.Handled = true;
    }
    private async void Group_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListViewItem { Tag: GroupRow group } || !e.DataView.Contains(BatchDragFormat)) return;
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            var ids = JsonSerializer.Deserialize<string[]>((await e.DataView.GetDataAsync(BatchDragFormat)) as string ?? "[]") ?? [];
            await ChangeAsync(() => App.Favorites.MoveManyAsync(ids, group.Id));
        }
        catch (Exception error) { Status.Text = error.Message; }
        finally { deferral.Complete(); }
    }
    private void Group_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListViewItem { Tag: GroupRow group } container || group.Id is null) return;
        var entry = App.Favorites.Entries.FirstOrDefault(entry => entry.Id == group.Id);
        if (entry is null) return;
        e.Handled = true;
        var menu = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = Loc.Get("RenameGroupMenu") };
        rename.Click += (_, _) => BeginNameEdit(entry);
        menu.Items.Add(rename);
        var up = new MenuFlyoutItem { Text = Loc.Get("Home_MoveUp") };
        up.Click += async (_, _) => await ChangeAsync(() => App.Favorites.ReorderAsync(entry.Id, -1));
        menu.Items.Add(up);
        var down = new MenuFlyoutItem { Text = Loc.Get("Home_MoveDown") };
        down.Click += async (_, _) => await ChangeAsync(() => App.Favorites.ReorderAsync(entry.Id, 1));
        menu.Items.Add(down);
        var remove = new MenuFlyoutItem { Text = Loc.Get("RemoveGroup") };
        remove.Click += async (_, _) =>
        {
            _groupId = null; Search.Text = ""; Refresh();
            Entries.SelectedItems.Clear();
            if (_rows.FirstOrDefault(row => row.Entry.Id == entry.Id) is { } row) Entries.SelectedItems.Add(row);
            await RemoveSelectedAsync();
        };
        menu.Items.Add(remove);
        menu.ShowAt(container, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }
}
