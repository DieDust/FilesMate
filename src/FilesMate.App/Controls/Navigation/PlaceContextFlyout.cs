using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.Platform.Windows.Shell;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.Navigation;

public static class PlaceContextFlyout
{
#if FILESMATE_UI_TEST
    internal static Func<string, CancellationToken, Task<DeviceEjectTarget?>>? TestEjectQuery;
#endif
    public static void Show(
        FrameworkElement anchor,
        NavigationItem item,
        Action<SidebarContextAction, NavigationItem, string> invoke,
        FlyoutPlacementMode placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        Windows.Foundation.Point? position = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(invoke);
        if (string.IsNullOrWhiteSpace(item.Target))
        {
            return;
        }

        var path = item.Target;
        var drive = item.Id.StartsWith("drive:", StringComparison.Ordinal);
        var device = PortableDeviceLocation.TryParse(path, out _);
        var actions = SidebarContextMenu.For(
            item,
            removableDrive: drive || device,
            networkDrive: drive && DriveShell.IsNetwork(path));
        if (actions.Count == 0)
        {
            return;
        }

        var menu = new MenuFlyout
        {
            Placement = placement,
            AreOpenCloseAnimationsEnabled = false,
        };
        if (TryStyle("FilesMate.SidebarFlyoutPresenterStyle", out var presenter))
        {
            menu.MenuFlyoutPresenterStyle = presenter;
        }

        SidebarContextAction? previous = null;
        MenuFlyoutItem? ejectItem = null;
        foreach (var action in actions)
        {
            if (previous is not null && SidebarContextMenu.Group(previous.Value) != SidebarContextMenu.Group(action))
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var (labelKey, glyph) = SidebarContextMenu.Describe(action);
            var flyoutItem = new MenuFlyoutItem
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
                flyoutItem.Style = style;
            }

            flyoutItem.Click += (_, _) => invoke(action, item, path);
            if (action == SidebarContextAction.Eject) { ejectItem = flyoutItem; ejectItem.IsEnabled = false; }
            menu.Items.Add(flyoutItem);
            previous = action;
        }

        var options = new FlyoutShowOptions { Placement = placement };
        if (position is { } point) options.Position = point;
        menu.ShowAt(anchor, options);
        if (ejectItem is not null) _ = ResolveEjectAsync(menu, ejectItem, path);
    }

    private static async Task ResolveEjectAsync(MenuFlyout menu, MenuFlyoutItem item, string path)
    {
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        void Closed(object? sender, object args) => request.Cancel();
        menu.Closed += Closed;
        try
        {
#if FILESMATE_UI_TEST
            var target = await (TestEjectQuery?.Invoke(path, request.Token)
                ?? SafeDeviceEject.QueryAsync(path, request.Token));
#else
            var target = await SafeDeviceEject.QueryAsync(path, request.Token);
#endif
            if (request.IsCancellationRequested) return;
            if (target is null) menu.Items.Remove(item);
            else item.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { App.LogFailure("EjectCapabilities", error); }
        finally { menu.Closed -= Closed; }
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
}
