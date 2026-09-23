namespace FilesMate.App.Navigation;

public enum SidebarContextAction
{
    OpenInNewTab,
    OpenInNewWindow,
    CopyPath,
    Unpin,
    Eject,
    DisconnectNetwork,
    WhoLocks,
    Properties,
    EditTag,
    DeleteTag,
    EditCloud,
    RemoveCloud,
    OpenTerminal,
}

public sealed class PlaceContextInvokedEventArgs : EventArgs
{
    public PlaceContextInvokedEventArgs(SidebarContextAction action, NavigationItem item, string path)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Action = action;
        Item = item;
        Path = path;
    }

    public SidebarContextAction Action { get; }

    public NavigationItem Item { get; }

    public string Path { get; }
}

public static class SidebarContextMenu
{
    public static IReadOnlyList<SidebarContextAction> For(
        NavigationItem item,
        bool removableDrive = false,
        bool networkDrive = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Target))
        {
            return [];
        }

        var actions = new List<SidebarContextAction>
        {
            SidebarContextAction.OpenInNewTab,
            SidebarContextAction.OpenInNewWindow,
        };

        if (IsTag(item))
        {
            actions.Add(SidebarContextAction.EditTag);
            actions.Add(SidebarContextAction.DeleteTag);
            return actions;
        }

        if (item.Id.StartsWith("cloud:", StringComparison.Ordinal))
        {
            actions.Add(SidebarContextAction.EditCloud);
            actions.Add(SidebarContextAction.RemoveCloud);
        }
        else if (CanUnpin(item))
        {
            actions.Add(SidebarContextAction.Unpin);
        }

        if (item.Id.StartsWith("drive:", StringComparison.Ordinal) || item.Id.StartsWith("device:", StringComparison.Ordinal))
        {
            if (removableDrive)
            {
                actions.Add(SidebarContextAction.Eject);
            }

            if (networkDrive)
            {
                actions.Add(SidebarContextAction.DisconnectNetwork);
            }
        }

        if (!IsVirtual(item.Target))
        {
            actions.Add(SidebarContextAction.OpenTerminal);
            actions.Add(SidebarContextAction.CopyPath);
            actions.Add(SidebarContextAction.WhoLocks);
            actions.Add(SidebarContextAction.Properties);
        }

        return actions;
    }

    public static (string LabelKey, string Glyph) Describe(SidebarContextAction action) => action switch
    {
        SidebarContextAction.OpenInNewTab => ("Command_OpenInNewTab", "\uE8A7"),
        SidebarContextAction.OpenInNewWindow => ("Command_OpenInNewWindow", "\uE8A7"),
        SidebarContextAction.OpenTerminal => ("Command_OpenInTerminal", "\uE756"),
        SidebarContextAction.CopyPath => ("Command_CopyPath", "\uE71B"),
        SidebarContextAction.Unpin => ("Command_UnpinFromSidebar", "\uE77A"),
        SidebarContextAction.Eject => ("Sidebar_Eject", "\uE88E"),
        SidebarContextAction.DisconnectNetwork => ("Sidebar_DisconnectNetworkDrive", "\uE8D7"),
        SidebarContextAction.WhoLocks => ("Command_WhoLocks", "\uE72E"),
        SidebarContextAction.Properties => ("Command_Properties", "\uE946"),
        SidebarContextAction.EditTag => ("Tag_Edit", "\uE70F"),
        SidebarContextAction.DeleteTag => ("Tag_Delete", "\uE74D"),
        SidebarContextAction.EditCloud => ("Sidebar_EditCloud", "\uE70F"),
        SidebarContextAction.RemoveCloud => ("Sidebar_RemoveCloud", "\uE8D7"),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static int Group(SidebarContextAction action) => action switch
    {
        SidebarContextAction.OpenInNewTab or SidebarContextAction.OpenInNewWindow => 0,
        SidebarContextAction.WhoLocks or SidebarContextAction.Properties => 2,
        _ => 1,
    };

    public static bool IsTag(NavigationItem item) =>
        TagLocation.TryParse(item.Target, out _)
        || item.Id.StartsWith("tag:", StringComparison.Ordinal);

    public static bool CanUnpin(NavigationItem item) =>
        item.Id != "home"
        && (WindowsNavigationSource.IsDefaultPinnedId(item.Id)
            || item.Id.StartsWith("custom:", StringComparison.Ordinal)
            || item.Id.StartsWith("cloud:custom:", StringComparison.Ordinal));

    public static bool IsVirtual(string? path) =>
        HomeLocation.IsHome(path) || TagLocation.IsTag(path)
        || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out _);
}
