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

public sealed class FavoritesBar : UserControl
{
    private const string DragFormat = "FilesMate.FavoriteId";
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly TextBlock _status = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid _layout = new() { ColumnSpacing = 6, Padding = new Thickness(4, 3, 4, 3) };
    private readonly Button _more = ButtonFor(Loc.Get("Favorites_More"), "\uE712");
    private bool _subscribed;
    private int _width;
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
        var add = ButtonFor(Loc.Get("Favorites_AddFolder"), "\uE734");
        Grid.SetColumn(_more, 1);
        _layout.Children.Add(_more);
        Grid.SetColumn(add, 2);
        add.Click += async (_, _) => await SaveCurrentFolderAsync(add);
        _layout.Children.Add(add);
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
        Visibility = App.Features.FavoritesBarEnabled ? Visibility.Visible : Visibility.Collapsed;
        _items.Children.Clear();
        if (Visibility != Visibility.Visible) return;
        var entries = App.Favorites.Entries.Where(entry => entry.GroupId is null).ToArray();
        // A large collection never creates thousands of buttons in the main window.
        var available = Math.Max(0, (ActualWidth > 0 ? ActualWidth : 600) - 128);
        var shown = 0;
        foreach (var entry in entries.Take(40))
        {
            var estimatedWidth = Math.Min(132, entry.Name.Sum(character => character > 255 ? 13 : 7)) + (entry.IsGroup ? 58 : 46);
            if (available < estimatedWidth) break;
            _items.Children.Add(EntryButton(entry));
            available -= estimatedWidth + 2;
            shown++;
        }
        _more.Visibility = entries.Length > shown ? Visibility.Visible : Visibility.Collapsed;
        _more.Tag = shown;
        _more.Click -= More_Click;
        _more.Click += More_Click;
        if (entries.Length == 0)
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
        button.DragStarting += (_, e) => { e.Data.SetData(DragFormat, entry.Id); e.Data.RequestedOperation = DataPackageOperation.Move; e.AllowedOperations = DataPackageOperation.Move; };
        button.DragOver += (_, e) => AcceptDrop(e);
        button.Drop += async (_, e) => await DropAsync(e, entry.IsGroup ? entry.Id : entry.GroupId, entry.Id);
        return () => dragged;
    }

    internal static IconElement IconFor(FavoriteEntry entry)
    {
        if (entry.IsGroup) return new ImageIcon { Source = new SvgImageSource(new Uri(GroupIconUri)), Width = 18, Height = 18 };
        var icon = new ImageIcon { Width = 18, Height = 18 };
        var version = 0;
        async void Refresh(object? sender, Models.AppearanceSettings settings)
        {
            var request = ++version;
            try
            {
                var source = await FileSurface.FileDragPreview.LoadIconAsync(entry.Path!, entry.IsDirectory, 18, icon.XamlRoot);
                if (request == version && icon.IsLoaded) icon.Source = source;
            }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Favorite icon: {0}", error.Message); }
        }
        icon.Loaded += (_, _) => { App.AppearanceChanged += Refresh; Refresh(null, Models.AppearanceSettings.Default); };
        icon.Unloaded += (_, _) => { ++version; App.AppearanceChanged -= Refresh; };
        return icon;
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
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph, FontSize = 16 } };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowEntries(FrameworkElement anchor, IEnumerable<FavoriteEntry> entries, string? groupId = null)
    {
        var menu = new MenuFlyout();
        var page = entries.ToArray();
        foreach (var entry in page.Take(60))
        {
            if (entry.IsGroup)
            {
                var sub = new MenuFlyoutSubItem { Text = entry.Name, Icon = IconFor(entry) };
                var children = App.Favorites.Entries.Where(item => item.GroupId == entry.Id).ToArray();
                foreach (var child in children.Take(60))
                    sub.Items.Add(EntryMenuItem(child));
                if (children.Length > 60) sub.Items.Add(MenuItem(Loc.Get("Favorites_MoreMenu"), "\uE712", () => ShowEntries(anchor, children.Skip(60), entry.Id)));
                if (sub.Items.Count == 0) sub.Items.Add(new MenuFlyoutItem { Text = Loc.Get("Favorites_GroupEmpty"), IsEnabled = false });
                menu.Items.Add(sub);
            }
            else menu.Items.Add(EntryMenuItem(entry));
        }
        if (page.Length > 60) menu.Items.Add(MenuItem(Loc.Get("Favorites_MoreMenu"), "\uE712", () => ShowEntries(anchor, page.Skip(60), groupId)));
        if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = Loc.Get("Favorites_GroupDrop"), IsEnabled = false });
        menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private MenuFlyoutItem EntryMenuItem(FavoriteEntry entry)
    {
        var item = new MenuFlyoutItem { Text = entry.Name, Icon = IconFor(entry) };
        ToolTipService.SetToolTip(item, entry.Path);
        var wasDragged = AttachEditingDrag(item, entry);
        item.Click += (_, _) => { if (!wasDragged()) OpenRequested?.Invoke(this, entry); };
        return item;
    }

    private void ShowBarMenu(FrameworkElement anchor, Windows.Foundation.Point? position = null)
    {
        var menu = new MenuFlyout();
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
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(Loc.Get("RenameMenu"), "\uE8AC", async () => await RenameAsync(entry)));
        menu.Items.Add(MenuItem(Loc.Get("MoveEarlier"), "\uE72B", async () => await SafeAsync(() => App.Favorites.ReorderAsync(entry.Id, -1))));
        menu.Items.Add(MenuItem(Loc.Get("MoveLater"), "\uE72A", async () => await SafeAsync(() => App.Favorites.ReorderAsync(entry.Id, 1))));
        if (!entry.IsGroup)
        {
            var move = new MenuFlyoutSubItem { Text = Loc.Get("MoveTo"), Icon = new FontIcon { Glyph = "\uE8DE" } };
            move.Items.Add(MenuItem(Loc.Get("Favorites_Bar"), "\uE734", async () => await SafeAsync(() => App.Favorites.MoveAsync(entry.Id, null))));
            foreach (var group in App.Favorites.Entries.Where(item => item.IsGroup))
                move.Items.Add(MenuItem(group.Name, "\uE8B7", async () => await SafeAsync(() => App.Favorites.MoveAsync(entry.Id, group.Id))));
            menu.Items.Add(move);
        }
        menu.Items.Add(MenuItem(Loc.Get("Favorites_ManageMenu"), "\uE713", async () => await ManageAsync(entry.IsGroup ? entry.Id : entry.GroupId)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem(Loc.Get("Favorites_Remove"), "\uE74D", async () => await RemoveAsync(entry)));
        menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private static void AcceptDrop(DragEventArgs e)
    {
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
                    var entry = App.Favorites.Entries.FirstOrDefault(item => item.Id == id);
                    await App.Favorites.MoveAsync(id, entry?.IsGroup == true ? null : groupId, beforeId);
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
        var groups = GroupChoices();
        var destination = new ComboBox { ItemsSource = groups, DisplayMemberPath = "Name", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = paths.Count == 1 ? System.IO.Path.GetFileName(paths[0].TrimEnd('\\')) : Loc.Format("Selection_Count", paths.Count), TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = Loc.Get("SaveTo") });
        panel.Children.Add(destination);
        panel.Children.Add(new TextBlock { Text = Loc.Get("Favorites_ReferenceHint"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var dialog = Dialog(Loc.Get("Favorites_Add"), panel, Loc.Get("Favorites_AddAction"));
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                var targets = await Task.Run(() => paths.Select(path => (path, Directory.Exists(path))).ToArray());
                await App.Favorites.AddAsync(targets, ((GroupChoice)destination.SelectedItem).Id);
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

    private async Task SaveCurrentFolderAsync(Button anchor)
    {
        if (CurrentFolder?.Invoke() is not { } path || !System.IO.Path.IsPathFullyQualified(path))
        {
            ShowError(Loc.Get("OpenFolderFirst"));
            return;
        }
        anchor.IsEnabled = false;
        try
        {
            var saved = await App.Favorites.SaveFolderAsync(path);
            if (!IsLoaded || XamlRoot is null) return;
            var groups = GroupChoices();
            var destination = new ComboBox
            {
                Header = Loc.Get("Favorites_AddTo"), ItemsSource = groups, DisplayMemberPath = nameof(GroupChoice.Name),
                SelectedItem = groups.First(group => group.Id == saved.GroupId),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(destination, Loc.Get("Favorites_AddTo"));
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            var panel = new StackPanel { Spacing = 12, Width = Math.Max(160, Math.Min(300, XamlRoot.Size.Width - 64)) };
            panel.Children.Add(new TextBlock { Text = Loc.Get("Favorites_Added"), FontSize = 18 });
            var name = new TextBlock { Text = saved.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTipService.SetToolTip(name, saved.Path);
            panel.Children.Add(name);
            panel.Children.Add(destination);
            panel.Children.Add(error);
            var flyout = new Flyout { Content = panel };
            var done = new Button { Content = Loc.Get("Tag_Done"), HorizontalAlignment = HorizontalAlignment.Right };
            done.Click += (_, _) => flyout.Hide();
            panel.Children.Add(done);
            var restoring = false;
            destination.SelectionChanged += async (_, _) =>
            {
                if (restoring || destination.SelectedItem is not GroupChoice choice || choice.Id == saved.GroupId) return;
                // Let ComboBox finish closing its list before changing enabled/focus state.
                await Task.Yield();
                destination.IsEnabled = done.IsEnabled = false;
                error.Visibility = Visibility.Collapsed;
                try
                {
                    await App.Favorites.MoveAsync(saved.Id, choice.Id);
                    saved = saved with { GroupId = choice.Id };
                }
                catch (Exception failure)
                {
                    restoring = true;
                    destination.SelectedItem = groups.First(group => group.Id == saved.GroupId);
                    restoring = false;
                    error.Text = failure.Message;
                    error.Visibility = Visibility.Visible;
                }
                finally { destination.IsEnabled = done.IsEnabled = true; }
            };
            anchor.IsEnabled = true;
            flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
        }
        catch (Exception failure) { ShowError(failure.Message); }
        finally { anchor.IsEnabled = true; }
    }

    private ContentDialog Dialog(string title, object content, string? primary = null)
    {
        var dialog = new ContentDialog { Title = title, Content = content, XamlRoot = XamlRoot, CloseButtonText = primary is null ? Loc.Get("Tag_Done") : Loc.Get("Cancel"),
            PrimaryButtonText = primary ?? "", DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary };
        ContentDialogTheme.Apply(dialog, this);
        return dialog;
    }

    private async Task NewGroupAsync() => await EditNameAsync(Loc.Get("Favorites_NewGroup"), "", App.Favorites.CreateGroupAsync);
    private async Task RenameAsync(FavoriteEntry entry) => await EditNameAsync(Loc.Get("Favorites_Rename"), entry.Name, name => App.Favorites.RenameAsync(entry.Id, name));
    private async Task EditNameAsync(string title, string initial, Func<string, Task> save)
    {
        var input = new TextBox { Text = initial, PlaceholderText = Loc.Get("Favorites_NameHint"), MaxLength = 120 };
        var panel = new StackPanel { Spacing = 10 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(input); panel.Children.Add(error);
        var dialog = Dialog(title, panel, Loc.Get("Confirm"));
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try { await save(input.Text); }
            catch (Exception failure) { e.Cancel = true; error.Text = failure.Message; }
            finally { deferral.Complete(); }
        };
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
        .Concat(App.Favorites.Entries.Where(entry => entry.IsGroup).Select(entry => new GroupChoice(entry.Id, entry.Name))).ToList();

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
