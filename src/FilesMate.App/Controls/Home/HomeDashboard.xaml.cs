using FilesMate.App.Controls.Navigation;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.Home;

public sealed partial class HomeDashboard : UserControl
{
    private int _reloadGeneration;
    private int _placesGeneration;
    private bool _released;
    private int _driveCount;
    private int _deviceCount;
    private readonly PinnedLocationStore _places = new(Program.SettingsPath(PinnedLocationStore.DefaultFilePath));
    private readonly HomeLayoutSettingsService _layoutStore = new(Program.SettingsPath(HomeLayoutSettingsService.DefaultFilePath));
    private readonly Dictionary<HomeSectionKind, bool> _sectionHasContent = [];
    private readonly Dictionary<HomeSectionKind, StackPanel> _sections;
    private HomeLayoutSettings _layout;

    public HomeDashboard()
    {
        InitializeComponent();
        _layout = _layoutStore.Load();
        _sections = new Dictionary<HomeSectionKind, StackPanel>
        {
            [HomeSectionKind.UserFolders] = UserFoldersSection,
            [HomeSectionKind.Drives] = DrivesSection,
            [HomeSectionKind.Cloud] = CloudSection,
            [HomeSectionKind.Tags] = TagsSection,
            [HomeSectionKind.System] = SystemSection,
        };
        TitleText.Text = StringTable.Get("Home");
        FoldersHeader.Text = StringTable.Get("Home_UserDirectories");
        DrivesHeader.Text = StringTable.Get("Home_Drives");
        CloudHeader.Text = StringTable.Get("Nav_Cloud");
        TagsHeader.Text = StringTable.Get("Nav_Tags");
        SearchHeader.Text = StringTable.Get("SettingsSearch");
        SystemHeader.Text = StringTable.Get("Home_SystemLocations");
        SystemHost.ItemsSource = new HomeFolderItem[]
        {
            new("system:recycle", StringTable.Get("System_RecycleBin"), "\uE74D", "shell:RecycleBinFolder"),
            new("system:network", StringTable.Get("System_Network"), "\uE968", "shell:NetworkPlacesFolder"),
            new("system:libraries", StringTable.Get("System_Libraries"), "\uE8F1", "shell:Libraries"),
            new("system:control", StringTable.Get("System_ControlPanel"), "\uE713", "shell:ControlPanelFolder"),
        };
        _sectionHasContent[HomeSectionKind.System] = true;
        ApplyLayout();
        AutomationProperties.SetName(RefreshButton, StringTable.Get("Command_Refresh"));
        ToolTipService.SetToolTip(RefreshButton, StringTable.Get("Command_Refresh"));
        AutomationProperties.SetName(CustomizeButton, StringTable.Get("Home_Customize"));
        ToolTipService.SetToolTip(CustomizeButton, StringTable.Get("Home_Customize"));
        Loaded += (_, _) => { App.DevicesChanged += DevicesChanged; Reload(); };
        Unloaded += (_, _) => { App.DevicesChanged -= DevicesChanged; _placesGeneration++; _reloadGeneration++; };
    }

    public event EventHandler<string>? PlaceChosen;

    public event EventHandler<PlaceContextInvokedEventArgs>? PlaceActionRequested;

    internal void ReleaseResources()
    {
        if (_released) return;
        _released = true;
        App.DevicesChanged -= DevicesChanged;
        _placesGeneration++;
        _reloadGeneration++;
        PlaceChosen = null;
        PlaceActionRequested = null;
        foreach (var host in new[] { FolderHost, DriveHost, DeviceHost, CloudHost, TagHost, SystemHost })
        {
            host.ItemsSource = null;
            host.ItemTemplate = new DataTemplate();
        }
        SearchResults.Children.Clear();
        SectionsHost.Children.Clear();
        _sections.Clear();
        Content = null;
    }

    public void Reload()
    {
        if (_released) return;
        var generation = ++_placesGeneration;
        _ = ReloadPlacesAsync(generation);
        _ = ReloadDrivesAsync(generation);
        _ = ReloadDevicesAsync(generation);
        _ = ReloadTagsAsync();
    }

    private async Task ReloadPlacesAsync(int generation)
    {
        try
        {
            var places = await Task.Run(() => (Folders: HomePlaces.UserFolders(), Cloud: HomePlaces.Cloud(_places)));
            if (generation != _placesGeneration || !IsLoaded) return;
            BindSection(HomeSectionKind.UserFolders, FolderHost, places.Folders);
            BindSection(HomeSectionKind.Cloud, CloudHost, places.Cloud);
        }
        catch (Exception error) { App.LogFailure("HomePlaces", error); }
    }

    private void DevicesChanged(object? sender, EventArgs args) => Reload();

    private async Task ReloadDevicesAsync(int generation)
    {
        DeviceHost.ItemsSource = null;
        _deviceCount = 0;
        SetSectionContent(HomeSectionKind.Drives, _driveCount > 0);
        try
        {
            var devices = await PortableDeviceCatalog.LoadAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (generation != _placesGeneration || !IsLoaded) return;
            DeviceHost.ItemsSource = devices.Select(device => new HomeFolderItem(
                "device:" + device.Root, device.RootName, "\uE8EA", device.Uri)).ToArray();
            _deviceCount = devices.Count;
            SetSectionContent(HomeSectionKind.Drives, _driveCount + _deviceCount > 0);
        }
        catch (Exception error) { App.LogFailure("HomeDevices", error); }
    }

    private async Task ReloadDrivesAsync(int generation)
    {
        try
        {
            var drives = await HomePlaces.LoadDrivesAsync();
            if (generation == _placesGeneration && IsLoaded)
            {
                DriveHost.ItemsSource = drives;
                _driveCount = drives.Count;
                SetSectionContent(HomeSectionKind.Drives, _driveCount + _deviceCount > 0);
            }
        }
        catch (Exception error) { App.LogFailure("HomeDrives", error); }
    }

    public void ShowSearchResults(IReadOnlyList<HomeSearchHit> hits)
    {
        if (_released) return;
        SearchResults.Children.Clear();
        if (hits.Count == 0)
        {
            SearchHost.Opacity = 0;
            SearchHost.IsHitTestVisible = false;
            return;
        }

        SearchHost.Opacity = 1;
        SearchHost.IsHitTestVisible = true;
        foreach (var hit in hits.Take(40))
        {
            var button = new Button
            {
                Tag = hit.Path,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = (Style)Application.Current.Resources["HomePlaceButtonStyle"],
                Padding = new Thickness(10, 8, 10, 8),
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = hit.Name, FontSize = 13 },
                        new TextBlock
                        {
                            Text = hit.Path,
                            FontSize = 11,
                            Style = (Style)Application.Current.Resources["FilesMate.SecondaryTextStyle"],
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                },
            };
            button.Click += Place_Click;
            button.ContextRequested += Place_ContextRequested;
            SearchResults.Children.Add(button);
        }
    }

    private async Task ReloadTagsAsync()
    {
        var generation = Interlocked.Increment(ref _reloadGeneration);
        IReadOnlyList<HomeFolderItem> tags = [];
        if (App.MetadataStore is { } store)
        {
            try
            {
                tags = HomePlaces.Tags(await store.ListTagsAsync().ConfigureAwait(true));
            }
            catch (Exception)
            {
            }
        }

        if (generation != _reloadGeneration)
        {
            return;
        }

        BindSection(HomeSectionKind.Tags, TagHost, tags);
    }

    private void BindSection(HomeSectionKind kind, ItemsRepeater host, IReadOnlyList<HomeFolderItem> items)
    {
        host.ItemsSource = items;
        SetSectionContent(kind, items.Count > 0);
    }

    private void SetSectionContent(HomeSectionKind kind, bool hasContent)
    {
        _sectionHasContent[kind] = hasContent;
        _sections[kind].Visibility = hasContent && !_layout.Hidden.Contains(kind)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyLayout()
    {
        SectionsHost.Children.Clear();
        foreach (var kind in _layout.Order)
        {
            var section = _sections[kind];
            section.Visibility = _sectionHasContent.GetValueOrDefault(kind)
                && !_layout.Hidden.Contains(kind)
                ? Visibility.Visible
                : Visibility.Collapsed;
            SectionsHost.Children.Add(section);
        }
    }

    private void CustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_released) return;
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        if (Application.Current.Resources.TryGetValue("FilesMate.MenuFlyoutPresenterStyle", out var style)
            && style is Style presenter)
        {
            menu.MenuFlyoutPresenterStyle = presenter;
        }

        for (var index = 0; index < _layout.Order.Count; index++)
        {
            var kind = _layout.Order[index];
            var group = new MenuFlyoutSubItem { Text = SectionName(kind) };
            var visible = new ToggleMenuFlyoutItem
            {
                Text = StringTable.Get("Home_ShowSection"),
                IsChecked = !_layout.Hidden.Contains(kind),
            };
            visible.Click += async (_, _) =>
                await ChangeLayoutAsync(_layout.SetVisible(kind, visible.IsChecked));
            var up = new MenuFlyoutItem
            {
                Text = StringTable.Get("Home_MoveUp"),
                IsEnabled = index > 0,
            };
            up.Click += async (_, _) => await ChangeLayoutAsync(_layout.Move(kind, -1));
            var down = new MenuFlyoutItem
            {
                Text = StringTable.Get("Home_MoveDown"),
                IsEnabled = index < _layout.Order.Count - 1,
            };
            down.Click += async (_, _) => await ChangeLayoutAsync(_layout.Move(kind, 1));
            group.Items.Add(visible);
            group.Items.Add(up);
            group.Items.Add(down);
            menu.Items.Add(group);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var reset = new MenuFlyoutItem { Text = StringTable.Get("Home_ResetLayout") };
        reset.Click += async (_, _) => await ChangeLayoutAsync(HomeLayoutSettings.Default);
        menu.Items.Add(reset);
        menu.ShowAt(CustomizeButton);
    }

    private async Task ChangeLayoutAsync(HomeLayoutSettings next)
    {
        if (_released) return;
        _layout = next;
        ApplyLayout();
        try
        {
            await _layoutStore.SaveAsync(next).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            App.LogFailure("HomeLayout", error);
        }
    }

    private string SectionName(HomeSectionKind kind) => kind switch
    {
        HomeSectionKind.UserFolders => FoldersHeader.Text,
        HomeSectionKind.Drives => DrivesHeader.Text,
        HomeSectionKind.Cloud => CloudHeader.Text,
        HomeSectionKind.Tags => TagsHeader.Text,
        HomeSectionKind.System => SystemHeader.Text,
        _ => kind.ToString(),
    };

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Reload();

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor && PlaceFrom(anchor)?.Target is { } path)
        {
            PlaceChosen?.Invoke(this, path);
        }
    }

    private void Place_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement anchor) return;
        ShowPlaceContext(anchor, e.TryGetPosition(anchor, out var point) ? point : null);
        e.Handled = true;
    }

    internal void ShowPlaceContext(FrameworkElement anchor, Windows.Foundation.Point? position = null)
    {
        if (PlaceFrom(anchor)?.Target is { } target && SpecialLocation.ShellName(target) is not null)
        {
            return;
        }
        if (PlaceFrom(anchor) is not { } item)
        {
            return;
        }

        PlaceContextFlyout.Show(
            anchor,
            item,
            (action, navigationItem, path) =>
                PlaceActionRequested?.Invoke(this, new PlaceContextInvokedEventArgs(action, navigationItem, path)),
            FlyoutPlacementMode.BottomEdgeAlignedLeft,
            position);
    }

    private static NavigationItem? PlaceFrom(FrameworkElement anchor) =>
        anchor.Tag switch
        {
            HomeFolderItem folder => HomePlaces.Place(folder),
            HomeDriveItem drive => HomePlaces.Place(drive),
            _ when anchor.DataContext is HomeFolderItem folder => HomePlaces.Place(folder),
            _ when anchor.DataContext is HomeDriveItem drive => HomePlaces.Place(drive),
            _ when anchor.Tag is string path && !string.IsNullOrWhiteSpace(path) =>
                new NavigationItem("place:" + path, PathCaption(path), "\uE8B7", path),
            _ => null,
        };

    private static string PathCaption(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}
