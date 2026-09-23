using System.Collections.ObjectModel;
using System.Globalization;

using FilesMate.App.Controls.Tags;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Shell;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;

using WinRT.Interop;

namespace FilesMate.App.Controls.Navigation;

public sealed partial class NavigationSidebar : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact),
        typeof(bool),
        typeof(NavigationSidebar),
        new PropertyMetadata(false, OnIsCompactChanged));

    private ObservableCollection<NavigationSection> _sections = [];
    private string? _selectedPath;
    private readonly PinnedLocationStore _pinnedLocations = new(Program.SettingsPath(PinnedLocationStore.DefaultFilePath));
    private int _reloadGeneration;
    private bool _sectionHeaderPressed;
    private double _savedScrollOffset;

    public NavigationSidebar()
    {
        InitializeComponent();
        SettingsLabel.Text = StringTable.Get("Settings");
        AutomationProperties.SetName(SettingsButton, StringTable.Get("Settings"));
        ToolTipService.SetToolTip(SettingsButton, StringTable.Get("Settings"));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler<string>? PlaceChosen;

    public event EventHandler<string>? OpenInNewTabRequested;

    public event EventHandler<string>? OpenInNewWindowRequested;

    public event EventHandler<string>? WhoLocksRequested;

    public event EventHandler? SettingsClicked;

    public event EventHandler? PinnedLocationsChanged;

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public void SelectPath(string path)
    {
        _selectedPath = path;
        NavigationCatalog.SelectPath(_sections, path);
    }

    public NavigationItem? PlaceFor(string? path, bool exact = false) =>
        NavigationCatalog.Match(_sections, path, exact);

    public void Reload() => _ = ReloadAsync(replaceWhenTagsEmpty: true);

    private void PublishPlaces(IReadOnlyList<NavigationSection> sections)
    {
        _sections = new ObservableCollection<NavigationSection>(
            sections);
        NavigationCatalog.SelectPath(_sections, _selectedPath);
        SectionRepeater.ItemsSource = _sections;
    }

    private async Task ReloadAsync(bool replaceWhenTagsEmpty)
    {
        var generation = Interlocked.Increment(ref _reloadGeneration);
        IReadOnlyList<NavigationItem> tags = [];
        if (App.MetadataStore is { } store)
        {
            try
            {
                var definitions = await store.ListTagsAsync().ConfigureAwait(true);
                tags = definitions
                    .OrderBy(tag => tag.SortOrder)
                    .ThenBy(tag => tag.Id)
                    .Select(tag => new NavigationItem(
                        "tag:" + tag.Id.ToString(CultureInfo.InvariantCulture),
                        tag.Name,
                        "\uE8EC",
                        TagLocation.Uri(tag.Id)))
                    .ToArray();
            }
            catch (Exception)
            {
            }
        }

        if (generation != _reloadGeneration)
        {
            return;
        }

        if (!replaceWhenTagsEmpty && tags.Count == 0)
        {
            return;
        }

        try
        {
            var sections = await Task.Run(() => WindowsNavigationSource.Create(_pinnedLocations, tags));
            if (generation == _reloadGeneration && IsLoaded) PublishPlaces(sections);
            else return;
            var devices = await Services.PortableDeviceCatalog.LoadAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (generation != _reloadGeneration || !IsLoaded) return;
            if (_sections.FirstOrDefault(section => section.Id == "drives") is { } drives)
                foreach (var device in devices)
                    drives.Items.Add(new NavigationItem("device:" + device.Root, device.RootName, "\uE8EA", device.Uri));
            NavigationCatalog.SelectPath(_sections, _selectedPath);
        }
        catch (Exception error) { App.LogFailure("NavigationPlaces", error); }
    }

    private void Place_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not NavigationItem item || item.Target is not { Length: > 0 } path)
        {
            return;
        }

        SelectPath(path);
        PlaceChosen?.Invoke(this, path);
    }

    private void Place_DragItemsStarting(object sender, DragItemsStartingEventArgs e) =>
        PlacesScroll.VerticalScrollMode = ScrollMode.Disabled;

    private void Place_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        PlacesScroll.VerticalScrollMode = ScrollMode.Auto;
        if ((sender.Tag as NavigationSection ?? sender.DataContext as NavigationSection) is not { } section)
        {
            return;
        }

        PersistItemOrder(section);
    }

    private void PersistItemOrder(NavigationSection section)
    {
        var ids = section.Items.Select(item => item.Id).ToArray();
        switch (section.Id)
        {
            case "pinned":
                _pinnedLocations.SaveItemOrder(ids);
                return;
            case "drives":
                _pinnedLocations.SaveDriveOrder(ids);
                return;
            case "cloud":
                _pinnedLocations.SaveCloudOrder(ids);
                return;
            case "tags":
                var tagIds = section.Items
                    .Select(item => TagLocation.TryParse(item.Target, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .ToArray();
                if (tagIds.Length > 0 && App.MetadataStore is { } store)
                {
                    Enqueue(() => store.ReorderTagsAsync(tagIds));
                }

                return;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsClicked?.Invoke(this, EventArgs.Empty);

    private void SectionHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _sectionHeaderPressed = false;
        ResetSectionDrag();
        if (sender is not FrameworkElement { Tag: NavigationSection section } || !section.ShowTitle)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() => section.IsExpanded = !section.IsExpanded);
    }

    private void SectionHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _sectionHeaderPressed = false;
        if (sender is not FrameworkElement header
            || header.Tag is not NavigationSection { Id: not "home" })
        {
            return;
        }

        var container = FindAncestor<ListViewItem>(header);
        if (container is null)
        {
            return;
        }

        container.CanDrag = true;
        _sectionHeaderPressed = true;
    }

    private void Section_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem container)
        {
            container.CanDrag = false;
        }
    }

    private void Section_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (!_sectionHeaderPressed
            || e.Items.OfType<NavigationSection>().Any(section => section.Id == "home"))
        {
            e.Cancel = true;
            ResetSectionDrag();
            return;
        }

        PlacesScroll.VerticalScrollMode = ScrollMode.Disabled;
    }

    private void Section_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        PlacesScroll.VerticalScrollMode = ScrollMode.Auto;
        ResetSectionDrag();
        var home = _sections.FirstOrDefault(section => section.Id == "home");
        if (home is not null)
        {
            var index = _sections.IndexOf(home);
            if (index > 0)
            {
                _sections.Move(index, 0);
            }
        }

        _pinnedLocations.SaveSectionOrder(_sections.Where(item => item.Id != "home").Select(item => item.Id));
    }

    private void ResetSectionDrag()
    {
        _sectionHeaderPressed = false;
        foreach (var section in _sections)
        {
            if (SectionRepeater.ContainerFromItem(section) is ListViewItem container)
            {
                container.CanDrag = false;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void Sidebar_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        PlacesScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

    private void Sidebar_PointerExited(object sender, PointerRoutedEventArgs e) =>
        PlacesScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

    private static void OnIsCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NavigationSidebar sidebar || e.NewValue is not true)
        {
            return;
        }

        foreach (var section in sidebar._sections)
        {
            section.IsExpanded = true;
        }
    }

    private void SectionAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not NavigationSection section)
        {
            return;
        }

        switch (section.Id)
        {
            case "pinned":
                _ = PinFolderAsync();
                return;
            case "tags":
                App.CurrentWindow?.OpenSettings("tags");
                return;
            case "cloud":
                var cloud = CreateSidebarFlyout();
                AddFlyoutItem(cloud, "Sidebar_AddCloudFolder", "\uE753", () => _ = AddCloudFolderAsync());
                AddFlyoutItem(cloud, "Sidebar_OneDriveSettings", "\uE713", () => _ = AddCloudAsync());
                cloud.ShowAt(button);
                return;
            case "drives":
                var menu = CreateSidebarFlyout();
                AddFlyoutItem(menu, "Sidebar_MapNetworkDrive", "\uE968", DriveShell.MapNetworkDrive);
                AddFlyoutItem(menu, "Sidebar_DisconnectNetworkDrive", "\uE8D7", DriveShell.DisconnectNetworkDrive);
                menu.ShowAt(button);
                return;
        }
    }

    private void Item_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var item = ItemFromSource(source);
        if (item is null && !e.TryGetPosition(sender, out _))
            item = ItemFromSource(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);
        if (sender is not ListView list || item is null
            || list.ContainerFromItem(item) is not FrameworkElement anchor
            || string.IsNullOrWhiteSpace(item.Target))
        {
            return;
        }

        PlaceContextFlyout.Show(anchor, item, Invoke,
            position: e.TryGetPosition(anchor, out var point) ? point : null);
        e.Handled = true;
    }

    private static NavigationItem? ItemFromSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: NavigationItem item })
            {
                return item;
            }
        }

        return null;
    }

    internal void InvokePlaceAction(SidebarContextAction action, NavigationItem item, string path) =>
        Invoke(action, item, path);

    private void Invoke(SidebarContextAction action, NavigationItem item, string path)
    {
        switch (action)
        {
            case SidebarContextAction.OpenInNewTab:
                OpenInNewTabRequested?.Invoke(this, path);
                return;
            case SidebarContextAction.OpenInNewWindow:
                OpenInNewWindowRequested?.Invoke(this, path);
                return;
            case SidebarContextAction.OpenTerminal:
                Enqueue(async () =>
                {
                    try { TerminalLaunch.Open(path); }
                    catch (Exception error)
                    {
                        await new ContentDialog { XamlRoot = XamlRoot, Content = error.Message,
                            CloseButtonText = StringTable.Get("Close") }.ShowAsync();
                    }
                });
                return;
            case SidebarContextAction.CopyPath:
                CopyPath(path);
                return;
            case SidebarContextAction.Unpin:
                Unpin(item);
                return;
            case SidebarContextAction.Eject:
                Enqueue(() => Views.DeviceEjectUI.ShowAsync(this, path));
                return;
            case SidebarContextAction.DisconnectNetwork:
                DriveShell.DisconnectLetter(path);
                return;
            case SidebarContextAction.WhoLocks:
                Enqueue(() =>
                {
                    WhoLocksRequested?.Invoke(this, path);
                    return Task.CompletedTask;
                });
                return;
            case SidebarContextAction.Properties:
                ShowProperties(path);
                return;
            case SidebarContextAction.EditTag:
                Enqueue(() => EditTagAsync(item));
                return;
            case SidebarContextAction.DeleteTag:
                Enqueue(() => DeleteTagAsync(item));
                return;
            case SidebarContextAction.EditCloud:
                Enqueue(() => EditCloudAsync(item));
                return;
            case SidebarContextAction.RemoveCloud:
                Enqueue(() => RemoveCloudAsync(path));
                return;
        }
    }

    private void Enqueue(Func<Task> work) =>
        _ = DispatcherQueue.TryEnqueue(() => _ = work());

    private static void CopyPath(string path)
    {
        var data = new DataPackage();
        data.SetText(path);
        Clipboard.SetContent(data);
    }

    private async Task RemoveCloudAsync(string path)
    {
        try
        {
            _pinnedLocations.HideCloud(path);
            App.NotifyPinnedLocationsChanged();
            PinnedLocationsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot, Content = StringTable.Get("Sidebar_CloudSaveFailed"),
                CloseButtonText = StringTable.Get("Close"),
            }.ShowAsync();
        }
    }

    private async Task EditCloudAsync(NavigationItem item)
    {
        var name = new TextBox { Header = StringTable.Get("Tag_Name"), Text = item.Label, MaxLength = 120 };
        var path = new TextBox { Header = StringTable.Get("Sidebar_CloudPath"), Text = item.Target, MaxLength = 32767 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var body = new StackPanel { Spacing = 16, MinWidth = 320 };
        body.Children.Add(name);
        body.Children.Add(path);
        body.Children.Add(new TextBlock { Text = StringTable.Get("Sidebar_CloudEntryHint"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = StringTable.Get("Sidebar_EditCloud"), Content = body,
            PrimaryButtonText = StringTable.Get("Save"), CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var target = path.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name.Text) || !System.IO.Path.IsPathFullyQualified(target)
                    || !await Task.Run(() => System.IO.Directory.Exists(target)))
                {
                    args.Cancel = true;
                    error.Text = StringTable.Get("Sidebar_CloudInvalid");
                    error.Visibility = Visibility.Visible;
                    return;
                }
                _pinnedLocations.EditCloud(item.Id, name.Text, target, item.Target);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
            {
                args.Cancel = true;
                error.Text = StringTable.Get("Sidebar_CloudSaveFailed");
                error.Visibility = Visibility.Visible;
            }
            finally { deferral.Complete(); }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.NotifyPinnedLocationsChanged();
            PinnedLocationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task EditTagAsync(NavigationItem item)
    {
        if (!TagLocation.TryParse(item.Target, out var id) || App.MetadataStore is null)
        {
            return;
        }

        try
        {
            var tag = (await App.MetadataStore.ListTagsAsync().ConfigureAwait(true))
                .FirstOrDefault(candidate => candidate.Id == id);
            if (tag is null)
            {
                return;
            }

            var value = await TagEditor.ShowAsync(XamlRoot, tag).ConfigureAwait(true);
            if (value is null || string.IsNullOrEmpty(value.Name))
            {
                return;
            }

            await App.MetadataStore.UpdateTagAsync(tag.Id, value.Name, value.Color).ConfigureAwait(true);
            App.NotifyTagsChanged();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Sidebar tag edit failed: {0}", error);
        }
    }

    private async Task DeleteTagAsync(NavigationItem item)
    {
        if (!TagLocation.TryParse(item.Target, out var id) || App.MetadataStore is null)
        {
            return;
        }

        try
        {
            if (!await TagEditor.ConfirmDeleteAsync(XamlRoot, item.Label).ConfigureAwait(true))
            {
                return;
            }

            await App.MetadataStore.DeleteTagAsync(id).ConfigureAwait(true);
            App.NotifyTagsChanged();
            if (TagLocation.TryParse(_selectedPath, out var current) && current == id)
            {
                PlaceChosen?.Invoke(this, HomeLocation.Uri);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Sidebar tag delete failed: {0}", error);
        }
    }

    private void Unpin(NavigationItem item)
    {
        if (item.Id.StartsWith("cloud:custom:", StringComparison.Ordinal))
        {
            _pinnedLocations.RemoveCloud(item.Target!);
        }
        else if (WindowsNavigationSource.IsDefaultPinnedId(item.Id))
        {
            _pinnedLocations.HideDefault(item.Id);
        }
        else
        {
            _pinnedLocations.Remove(item.Target!);
        }

        App.NotifyPinnedLocationsChanged();
        PinnedLocationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task PinFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        if (App.CurrentWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, window.NativeHandle);
        }

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        WindowsNavigationSource.Pin(folder.Path, _pinnedLocations);
        App.NotifyPinnedLocationsChanged();
        PinnedLocationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AddCloudFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        if (App.CurrentWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, window.NativeHandle);
        }

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        _pinnedLocations.AddCloud(folder.Path);
        App.NotifyPinnedLocationsChanged();
        PinnedLocationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task AddCloudAsync() =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:onedrive"));

    private static void ShowProperties(string path)
    {
        try
        {
            new WindowsLocalFileOperations().ShowProperties(path);
        }
        catch (Exception)
        {
        }
    }

    private MenuFlyout CreateSidebarFlyout()
    {
        var menu = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            AreOpenCloseAnimationsEnabled = false,
        };
        if (TryStyle("FilesMate.SidebarFlyoutPresenterStyle", out var presenter))
        {
            menu.MenuFlyoutPresenterStyle = presenter;
        }

        return menu;
    }

    private static void AddFlyoutItem(MenuFlyout menu, string labelKey, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = StringTable.Get(labelKey),
            Icon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 14,
            },
        };
        if (TryStyle("FilesMate.SidebarFlyoutItemStyle", out var style))
        {
            item.Style = style;
        }

        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static bool TryStyle(string key, out Style style)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style found)
        {
            style = found;
            return true;
        }

        style = null!;
        return false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SectionRepeater.ItemsSource = _sections;
        App.PinnedLocationsChanged -= App_PinnedLocationsChanged;
        App.PinnedLocationsChanged += App_PinnedLocationsChanged;
        App.TagsChanged -= App_TagsChanged;
        App.TagsChanged += App_TagsChanged;
        App.DevicesChanged -= App_DevicesChanged;
        App.DevicesChanged += App_DevicesChanged;
        await ReloadAsync(replaceWhenTagsEmpty: true);
        if (IsLoaded)
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded) PlacesScroll.ChangeView(null, _savedScrollOffset, null, disableAnimation: true);
            });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _savedScrollOffset = PlacesScroll.VerticalOffset;
        Interlocked.Increment(ref _reloadGeneration);
        // Keep lightweight navigation state, not per-tab realized item templates.
        SectionRepeater.ItemsSource = null;
        App.PinnedLocationsChanged -= App_PinnedLocationsChanged;
        App.TagsChanged -= App_TagsChanged;
        App.DevicesChanged -= App_DevicesChanged;
    }

    private void App_PinnedLocationsChanged(object? sender, EventArgs e) => Reload();

    private void App_TagsChanged(object? sender, EventArgs e) => Reload();
    private void App_DevicesChanged(object? sender, EventArgs e) => Reload();

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var items = VisibleItems().ToList();
        if (items.Count == 0)
        {
            return;
        }

        var index = items.FindIndex(item => item.Selected);
        if (index < 0)
        {
            index = 0;
        }

        var next = e.Key switch
        {
            Windows.System.VirtualKey.Up => Math.Max(0, index - 1),
            Windows.System.VirtualKey.Down => Math.Min(items.Count - 1, index + 1),
            Windows.System.VirtualKey.Home => 0,
            Windows.System.VirtualKey.End => items.Count - 1,
            Windows.System.VirtualKey.Enter => index,
            _ => -1,
        };
        if (next < 0)
        {
            return;
        }

        var item = items[next];
        if (item.Target is null)
        {
            return;
        }

        SelectPath(item.Target);
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            PlaceChosen?.Invoke(this, item.Target);
        }

        e.Handled = true;
    }

    private IEnumerable<NavigationItem> VisibleItems()
    {
        foreach (var section in _sections)
        {
            if (section.ShowTitle && !section.IsExpanded)
            {
                continue;
            }

            foreach (var item in section.Items)
            {
                if (!string.IsNullOrEmpty(item.Target))
                {
                    yield return item;
                }
            }
        }
    }

}
