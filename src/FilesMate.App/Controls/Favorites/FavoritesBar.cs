using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Icons;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FilesMate.App.Controls.Favorites;

public sealed partial class FavoritesBar : UserControl
{
    private const string DragFormat = "FilesMate.FavoriteId";
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly TextBlock _status = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid _layout = new() { ColumnSpacing = 6, Padding = new Thickness(4, 3, 4, 3) };
    private readonly Button _more = ButtonFor(Loc.Get("Favorites_More"), "\uE712");
    private bool _subscribed;
    private int _width;
    private FavoriteEntry[]? _renderedEntries;
    private bool _renderedEmptyHint;
    public Func<string?>? CurrentFolder { get; set; }
    public event EventHandler<FavoriteEntry>? OpenRequested;

    public FavoritesBar()
    {
        _layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var scroll = new ScrollViewer { Content = _items, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _layout.Children.Add(scroll);
        Grid.SetColumn(_more, 1);
        _layout.Children.Add(_more);
        Grid.SetColumn(_bookmark, 2);
        _bookmark.Click += async (_, _) => await EditCurrentFolderAsync();
        _layout.Children.Add(_bookmark);
        var surface = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Background="{ThemeResource FilesMate.Favorites.BackgroundBrush}"
                Margin="8,0,8,0" CornerRadius="8" />
            """);
        surface.Child = _layout;
        Content = surface;
        AllowDrop = true;
        DragOver += (_, e) => AcceptDrop(e);
        Drop += async (_, e) => await DropAsync(e, null, null);
        ContextRequested += (_, e) =>
        {
            if (e.Handled) return;
            ShowBarMenu(this, e.TryGetPosition(this, out var point) ? point : null);
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            if (!_subscribed) { App.Favorites.Changed += Changed; App.FeaturesChanged += Changed; _subscribed = true; }
            Render();
        };
        Unloaded += (_, _) =>
        {
            _currentFolderFlyout?.Hide();
            App.Favorites.Changed -= Changed;
            App.FeaturesChanged -= Changed;
            _subscribed = false;
        };
        Visibility = App.Features.FavoritesBarEnabled ? Visibility.Visible : Visibility.Collapsed;
        SizeChanged += (_, _) => { if (_width != (int)ActualWidth) { _width = (int)ActualWidth; Render(); } };
    }

    private void Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        RefreshCurrentFolder();
        Visibility = App.Features.FavoritesBarEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (Visibility != Visibility.Visible) return;
        var entries = App.Favorites.Entries.Where(entry => entry.GroupId is null).ToArray();
        // A large collection never creates thousands of buttons in the main window.
        var available = Math.Max(0, (ActualWidth > 0 ? ActualWidth : 600) - 128);
        var shown = 0;
        foreach (var entry in entries.Take(40))
        {
            var estimatedWidth = Math.Min(132, entry.Name.Sum(character => character > 255 ? 13 : 7)) + (entry.IsGroup ? 58 : 46);
            if (available < estimatedWidth) break;
            available -= estimatedWidth + 2;
            shown++;
        }
        _more.Visibility = entries.Length > shown ? Visibility.Visible : Visibility.Collapsed;
        _more.Tag = shown;
        _more.Click -= More_Click;
        _more.Click += More_Click;
        var visibleEntries = entries.Take(shown).ToArray();
        // Layout can change slightly when the active pane settles. Keep decoded
        // icons and controls when the same bookmarks still fit in the bar.
        var showEmptyHint = entries.Length == 0;
        if (_renderedEntries is not null && _renderedEmptyHint == showEmptyHint
            && _renderedEntries.SequenceEqual(visibleEntries)) return;
        _renderedEntries = visibleEntries;
        _renderedEmptyHint = showEmptyHint;
        _items.Children.Clear();
        foreach (var entry in visibleEntries) _items.Children.Add(EntryButton(entry));
        if (showEmptyHint)
        {
            var hint = new Button { Content = new TextBlock { Text = Loc.Get("Favorites_DropHint"), FontSize = 13, Opacity = 0.72 }, Padding = new Thickness(8, 5, 8, 5), MinHeight = 30,
                Background = null, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6) };
            hint.Click += async (_, _) => await AddCurrentAsync();
            _items.Children.Add(hint);
        }
        if (App.Favorites.LoadError is { } error) ShowError(error);
    }

    private void More_Click(object sender, RoutedEventArgs e) => ShowEntries(_more,
        App.Favorites.Entries.Where(entry => entry.GroupId is null).Skip((int)(_more.Tag ?? 0)));

    private Button EntryButton(FavoriteEntry entry)
    {
        var contents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        contents.Children.Add(IconFor(entry));
        contents.Children.Add(new TextBlock { Text = entry.Name, MaxWidth = 132, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (entry.IsGroup) contents.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = contents, MinHeight = 30, Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6), Background = null, BorderThickness = new Thickness(0), CanDrag = true, AllowDrop = true };
        AutomationProperties.SetName(button, entry.Name);
        ToolTipService.SetToolTip(button, entry.Path ?? Loc.Format("Favorites_GroupTooltip", entry.Name));
        var wasDragged = AttachEditingDrag(button, entry);
        button.Click += (_, _) => { if (wasDragged()) return; if (entry.IsGroup) ShowEntries(button, App.Favorites.Entries.Where(item => item.GroupId == entry.Id), entry.Id); else OpenRequested?.Invoke(this, entry); };
        return button;
    }

    private Func<bool> AttachEditingDrag(Control button, FavoriteEntry entry)
    {
        button.CanDrag = true;
        button.AllowDrop = true;
        var pressed = false;
        var dragged = false;
        var origin = new Windows.Foundation.Point();
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            pressed = point.Properties.IsLeftButtonPressed;
            dragged = false;
            origin = point.Position;
        }), true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            if (!pressed || !point.Properties.IsLeftButtonPressed || Math.Abs(point.Position.X - origin.X) + Math.Abs(point.Position.Y - origin.Y) < 6) return;
            pressed = false;
            dragged = true;
            button.ReleasePointerCaptures();
            try { await button.StartDragAsync(point); }
            catch (Exception error) { ShowError(error.Message); }
        }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => pressed = false), true);
        button.KeyDown += (_, _) => dragged = false;
        button.ContextRequested += (_, e) => { ShowEntryMenu(button, entry); e.Handled = true; };
        button.DragStarting += (_, e) => { e.Data.SetData(DragFormat, entry.Id); e.Data.Properties[DragFormat] = entry.Id; e.Data.RequestedOperation = DataPackageOperation.Move; e.AllowedOperations = DataPackageOperation.Move; };
        button.DragOver += (_, e) => AcceptDrop(e, entry.IsGroup ? entry.Id : entry.GroupId);
        button.Drop += async (_, e) => await DropAsync(e, entry.IsGroup ? entry.Id : entry.GroupId, entry.Id);
        return () => dragged;
    }

    internal static IconElement IconFor(FavoriteEntry entry)
    {
        if (entry.IsGroup) return new ImageIcon { Source = new SvgImageSource(new Uri(GroupIconUri)), Width = 18, Height = 18 };
        var icon = new ImageIcon { Width = 18, Height = 18 };
        void Refresh(object? sender, Models.AppearanceSettings settings)
        {
            // Opening a bookmark menu must not resolve shortcuts, probe offline
            // paths or activate Shell providers. Cached native icons are safe;
            // otherwise use the small bundled format icon immediately.
            icon.Source = CachedIconSource(entry, 18, icon.XamlRoot);
        }
        icon.Loaded += (_, _) => { App.AppearanceChanged += Refresh; Refresh(null, Models.AppearanceSettings.Default); };
        icon.Unloaded += (_, _) => App.AppearanceChanged -= Refresh;
        return icon;
    }

    internal static Microsoft.UI.Xaml.Media.ImageSource CachedIconSource(FavoriteEntry entry, int size, XamlRoot? root)
    {
        if (entry.IsGroup) return new SvgImageSource(new Uri(GroupIconUri));
        var kind = FileTypeIconCatalog.ClassifyPath(entry.Path!, entry.IsDirectory);
        var cached = (!ShellIconBinder.UseBundledIcons || kind is null || FileTypeIconCatalog.PrefersShell(kind.Value))
            ? ShellIconBinder.Service.TryGetCached(FilesMate.Core.Icons.IconKey.ForPath(entry.Path!, entry.IsDirectory,
                ShellIconBinder.RasterizePixelSize(root, size))) : null;
        return cached is not null ? ShellIconBinder.ToBitmap(cached)
            : new SvgImageSource(new Uri(FileTypeIconCatalog.AssetUri(kind ?? FileIconKind.Generic)));
    }

    internal const string GroupIconUri = "ms-appx:///Assets/FileIcons/bookmark-group.svg";

    private static Button ButtonFor(string name, string glyph)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 16 }, MinHeight = 30, MinWidth = 30,
            Padding = new Thickness(6), Background = null, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6) };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new FavoriteMenuItem { Text = text, Icon = new FontIcon { Glyph = glyph, FontSize = 16 } };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowEntries(FrameworkElement anchor, IEnumerable<FavoriteEntry> entries, string? groupId = null)
    {
        var menu = CreateMenu();
        var page = entries.ToArray();
        foreach (var entry in page.Take(60)) menu.Items.Add(EntryMenuItem(entry, anchor));
        if (page.Length > 60) menu.Items.Add(MenuItem(Loc.Get("Favorites_MoreMenu"), "\uE712", () => ShowEntries(anchor, page.Skip(60), groupId)));
        if (menu.Items.Count == 0) menu.Items.Add(new FavoriteMenuItem { Text = Loc.Get("Favorites_GroupEmpty"), IsEnabled = false });
        if (groupId is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(Loc.Get("Favorites_NewSubgroup"), "\uE8F4", async () => await NewGroupAsync(groupId)));
            menu.Items.Add(MenuItem(Loc.Get("Favorites_ManageMenu"), "\uE713", async () => await ManageAsync(groupId)));
        }
        menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private MenuFlyoutItemBase EntryMenuItem(FavoriteEntry entry, FrameworkElement anchor)
    {
        if (entry.IsGroup)
        {
            var sub = new MenuFlyoutSubItem { Text = entry.Name, Icon = IconFor(entry), MinHeight = 32, FontSize = 13, Padding = new Thickness(8, 0, 8, 0) };
            // Only build the next level when its parent menu becomes visible.
            // Deep groups never materialize the entire tree on a bar click.
            sub.Loaded += (_, _) =>
            {
                sub.Items.Clear();
                var children = App.Favorites.Entries.Where(item => item.GroupId == entry.Id).ToArray();
                foreach (var child in children.Take(60)) sub.Items.Add(EntryMenuItem(child, anchor));
                if (children.Length > 60) sub.Items.Add(MenuItem(Loc.Get("Favorites_MoreMenu"), "\uE712", () => ShowEntries(anchor, children.Skip(60), entry.Id)));
                if (sub.Items.Count == 0) sub.Items.Add(new FavoriteMenuItem { Text = Loc.Get("Favorites_GroupEmpty"), IsEnabled = false });
                sub.Items.Add(new MenuFlyoutSeparator());
                sub.Items.Add(MenuItem(Loc.Get("Favorites_NewSubgroup"), "\uE8F4", async () => await NewGroupAsync(entry.Id)));
                sub.Items.Add(MenuItem(Loc.Get("Favorites_ManageMenu"), "\uE713", async () => await ManageAsync(entry.Id)));
            };
            AttachEditingDrag(sub, entry);
            return sub;
        }
        var item = new FavoriteMenuItem { Text = entry.Name, Icon = IconFor(entry) };
        ToolTipService.SetToolTip(item, entry.Path);
        var wasDragged = AttachEditingDrag(item, entry);
        item.Click += (_, _) => { if (!wasDragged()) OpenRequested?.Invoke(this, entry); };
        return item;
    }

    private void ShowBarMenu(FrameworkElement anchor, Windows.Foundation.Point? position = null)
    {
        var menu = CreateMenu();
        menu.Items.Add(MenuItem(Loc.Get("Favorites_AddFolder"), "\uE734", async () => await AddCurrentAsync()));
        menu.Items.Add(MenuItem(Loc.Get("Favorites_NewGroupMenu"), "\uE8F4", async () => await NewGroupAsync()));
        menu.Items.Add(MenuItem(Loc.Get("Favorites_ManageMenu"), "\uE713", async () => await ManageAsync()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(Loc.Get("Favorites_Hide"), "\uE76C", () => Safe(() => App.SetFavoritesBarEnabled(false))));
        var options = new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        if (position is { } point) options.Position = point;
        menu.ShowAt(anchor, options);
    }

    private void ShowEntryMenu(FrameworkElement anchor, FavoriteEntry entry)
    {
        var menu = CreateMenu();
        menu.Items.Add(MenuItem(Loc.Get("RenameMenu"), "\uE8AC", async () => await RenameAsync(entry)));
        menu.Items.Add(MenuItem(Loc.Get("MoveEarlier"), "\uE72B", async () => await SafeAsync(() => App.Favorites.ReorderAsync(entry.Id, -1))));
        menu.Items.Add(MenuItem(Loc.Get("MoveLater"), "\uE72A", async () => await SafeAsync(() => App.Favorites.ReorderAsync(entry.Id, 1))));
        if (entry.IsGroup)
            menu.Items.Add(MenuItem(Loc.Get("Favorites_NewSubgroup"), "\uE8F4", async () => await NewGroupAsync(entry.Id)));
        var move = new MenuFlyoutSubItem { Text = Loc.Get("MoveTo"), Icon = new FontIcon { Glyph = "\uE8DE" } };
        foreach (var group in GroupChoices().Where(group => App.Favorites.CanMoveTo([entry.Id], group.Id)))
            move.Items.Add(MenuItem(group.Name, "\uE8B7", async () => await SafeAsync(() => App.Favorites.MoveAsync(entry.Id, group.Id))));
        menu.Items.Add(move);
        menu.Items.Add(MenuItem(Loc.Get("Favorites_ManageMenu"), "\uE713", async () => await ManageAsync(entry.IsGroup ? entry.Id : entry.GroupId)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(Loc.Get("Favorites_Remove"), "\uE74D", async () => await RemoveAsync(entry)));
        menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private static void AcceptDrop(DragEventArgs e, string? groupId = null)
    {
        if (e.DataView.Properties.TryGetValue(DragFormat, out var value) && value is string id && !App.Favorites.CanMoveTo([id], groupId))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }
        if (e.DataView.Contains(DragFormat)) e.AcceptedOperation = DataPackageOperation.Move;
        else if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
        else return;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = e.DataView.Contains(DragFormat) ? Loc.Get("Favorites_Move") : Loc.Get("Favorites_Add");
        e.Handled = true;
    }

    private async Task DropAsync(DragEventArgs e, string? groupId, string? beforeId)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(DragFormat))
            {
                var id = await e.DataView.GetDataAsync(DragFormat) as string;
                if (id is not null)
                {
                    await App.Favorites.MoveAsync(id, groupId, beforeId);
                }
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                await App.Favorites.AddAsync(items.Where(item => !string.IsNullOrEmpty(item.Path)).Select(item => (item.Path, item is StorageFolder)), groupId);
            }
        }
        catch (Exception error) { ShowError(error.Message); }
        finally { deferral.Complete(); }
    }

    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        var destination = new FavoriteGroupPicker();
        AutomationProperties.SetAutomationId(destination, "FavoriteGroup");
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = paths.Count == 1 ? System.IO.Path.GetFileName(paths[0].TrimEnd('\\')) : Loc.Format("Selection_Count", paths.Count), TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = Loc.Get("SaveTo") });
        panel.Children.Add(destination);
        panel.Children.Add(new TextBlock { Text = Loc.Get("Favorites_ReferenceHint"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var dialog = Dialog(Loc.Get("Favorites_Add"), panel, Loc.Get("Favorites_AddAction"));
        AutomationProperties.SetAutomationId(dialog, "AddFavoritesDialog");
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            if (destination.IsSaving) { e.Cancel = true; return; }
            var deferral = e.GetDeferral();
            try
            {
                var targets = await Task.Run(() => paths.Select(path => (path, Directory.Exists(path))).ToArray());
                await App.Favorites.AddAsync(targets, destination.SelectedGroupId);
                App.SetFavoritesBarEnabled(true);
            }
            catch (Exception error) { e.Cancel = true; panel.Children.Add(new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap }); }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
    }

    private async Task AddCurrentAsync()
    {
        if (CurrentFolder?.Invoke() is { } path && System.IO.Path.IsPathFullyQualified(path)) await AddPathsAsync([path]);
        else ShowError(Loc.Get("OpenFolderFirst"));
    }

    private ContentDialog Dialog(string title, object content, string? primary = null)
    {
        var dialog = new ContentDialog { Title = title, Content = content, XamlRoot = XamlRoot, CloseButtonText = primary is null ? Loc.Get("Tag_Done") : Loc.Get("Cancel"),
            PrimaryButtonText = primary ?? "", DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary };
        ContentDialogTheme.Apply(dialog, this);
        return dialog;
    }

    private async Task NewGroupAsync(string? parentId = null) => await EditNameAsync(Loc.Get(parentId is null ? "Favorites_NewGroup" : "Favorites_NewSubgroup"), "",
        name => App.Favorites.CreateGroupAsync(name, parentId));
    private async Task RenameAsync(FavoriteEntry entry) => await EditNameAsync(Loc.Get("Favorites_Rename"), entry.Name, name => App.Favorites.RenameAsync(entry.Id, name));
    private async Task EditNameAsync(string title, string initial, Func<string, Task> save)
    {
        var dialog = new FavoriteNameDialog(title, initial, save) { XamlRoot = XamlRoot };
        ContentDialogTheme.Apply(dialog, this);
        await dialog.ShowAsync();
    }

    private async Task RemoveAsync(FavoriteEntry entry)
    {
        if (entry.IsGroup && App.Favorites.Entries.Any(item => item.GroupId == entry.Id)
            && await Dialog(Loc.Get("Favorites_RemoveGroup"), new TextBlock { Text = Loc.Format("Favorites_RemoveGroupConfirm", entry.Name), TextWrapping = TextWrapping.Wrap }, Loc.Get("Remove")).ShowAsync() != ContentDialogResult.Primary) return;
        await SafeAsync(() => App.Favorites.RemoveAsync(entry.Id));
    }

    private sealed record GroupChoice(string? Id, string Name);
    private static List<GroupChoice> GroupChoices() => new[] { new GroupChoice(null, Loc.Get("Favorites_Bar")) }
        .Concat(App.Favorites.GroupsInTreeOrder().Select(group => new GroupChoice(group.Entry.Id, group.Breadcrumb))).ToList();

    private sealed class FavoriteMenuItem : MenuFlyoutItem
    {
        public FavoriteMenuItem()
        {
            ProtectedCursor = Input.DesktopCursors.Arrow;
            if (Application.Current.Resources.TryGetValue("FilesMate.SidebarFlyoutItemStyle", out var style) && style is Style itemStyle)
                Style = itemStyle;
            MinHeight = 32;
            FontSize = 13;
        }
    }

    internal static MenuFlyout CreateMenu()
    {
        var menu = new MenuFlyout { AreOpenCloseAnimationsEnabled = false };
        if (Application.Current.Resources.TryGetValue("FilesMate.SidebarFlyoutPresenterStyle", out var style) && style is Style presenterStyle)
        {
            var compact = new Style(typeof(MenuFlyoutPresenter)) { BasedOn = presenterStyle };
            compact.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 180d));
            compact.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 360d));
            menu.MenuFlyoutPresenterStyle = compact;
        }
        FlyoutTheme.FollowHost(menu);
        return menu;
    }


    private async Task ManageAsync(string? groupId = null)
    {
        var manager = new FavoritesManager(groupId);
        manager.OpenRequested += (_, entry) => OpenRequested?.Invoke(this, entry);
        var dialog = new FavoritesManagerDialog(manager, XamlRoot);
        AutomationProperties.SetName(dialog, Loc.Get("Favorites_Manage"));
        ContentDialogTheme.Apply(dialog, this);
        manager.CloseRequested += (_, _) => dialog.Hide();
        await dialog.ShowAsync();
    }

    private void ShowError(string message)
    {
        _status.Text = message;
        ToolTipService.SetToolTip(_status, message);
        if (!_items.Children.Contains(_status)) _items.Children.Add(_status);
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { ShowError(error.Message); } }
    private async Task SafeAsync(Func<Task> action) { try { await action(); } catch (Exception error) { ShowError(error.Message); } }
}
