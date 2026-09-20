using System.Runtime.InteropServices;
namespace FilesMate.Platform.Windows.Shell;

// Windows SDK ShObjIdl_core.h vtable order, including inherited methods.
[ComVisible(true), Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISelectionFolderView2
{
    [PreserveSig] public int GetCurrentViewMode(out uint mode);
    [PreserveSig] public int SetCurrentViewMode(uint mode);
    [PreserveSig] public int GetFolder(ref Guid iid, out nint result);
    [PreserveSig] public int Item(int index, out nint item);
    [PreserveSig] public int ItemCount(uint flags, out int count);
    [PreserveSig] public int Items(uint flags, ref Guid iid, out nint result);
    [PreserveSig] public int GetSelectionMarkedItem(out int index);
    [PreserveSig] public int GetFocusedItem(out int index);
    [PreserveSig] public int GetItemPosition(nint item, nint point);
    [PreserveSig] public int GetSpacing(nint point);
    [PreserveSig] public int GetDefaultSpacing(nint point);
    [PreserveSig] public int GetAutoArrange();
    [PreserveSig] public int SelectItem(int index, uint flags);
    [PreserveSig] public int SelectAndPositionItems(uint count, nint items, nint points, uint flags);
    [PreserveSig] public int SetGroupBy(nint key, int ascending);
    [PreserveSig] public int GetGroupBy(nint key, nint ascending);
    [PreserveSig] public int SetViewProperty(nint item, nint key, nint value);
    [PreserveSig] public int GetViewProperty(nint item, nint key, nint value);
    [PreserveSig] public int SetTileViewProperties(nint item, nint text);
    [PreserveSig] public int SetExtendedTileViewProperties(nint item, nint text);
    [PreserveSig] public int SetText(int type, nint text);
    [PreserveSig] public int SetCurrentFolderFlags(uint mask, uint flags);
    [PreserveSig] public int GetCurrentFolderFlags(out uint flags);
    [PreserveSig] public int GetSortColumnCount(out int count);
    [PreserveSig] public int SetSortColumns(nint columns, int count);
    [PreserveSig] public int GetSortColumns(nint columns, int count);
    [PreserveSig] public int GetItem(int index, ref Guid iid, out nint result);
    [PreserveSig] public int GetVisibleItem(int start, int previous, out int index);
    [PreserveSig] public int GetSelectedItem(int start, out int index);
    [PreserveSig] public int GetSelection(int noneImpliesFolder, out nint result);
    [PreserveSig] public int GetSelectionState(nint item, out uint flags);
    [PreserveSig] public int InvokeVerbOnSelection(nint verb);
    [PreserveSig] public int SetViewModeAndIconSize(int mode, int size);
    [PreserveSig] public int GetViewModeAndIconSize(out int mode, out int size);
    [PreserveSig] public int SetGroupSubsetCount(uint rows);
    [PreserveSig] public int GetGroupSubsetCount(out uint rows);
    [PreserveSig] public int SetRedraw(int redraw);
    [PreserveSig] public int IsMoveInSameFolder();
    [PreserveSig] public int DoRename();
}

[ComVisible(true), Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISelectionShellBrowser
{
    [PreserveSig] public int GetWindow(out nint window);
    [PreserveSig] public int ContextSensitiveHelp(int enter);
    [PreserveSig] public int InsertMenusSB(nint menu, nint widths);
    [PreserveSig] public int SetMenuSB(nint menu, nint hole, nint active);
    [PreserveSig] public int RemoveMenusSB(nint menu);
    [PreserveSig] public int SetStatusTextSB(nint text);
    [PreserveSig] public int EnableModelessSB(int enable);
    [PreserveSig] public int TranslateAcceleratorSB(nint message, ushort id);
    [PreserveSig] public int BrowseObject(nint item, uint flags);
    [PreserveSig] public int GetViewStateStream(uint mode, out nint stream);
    [PreserveSig] public int GetControlWindow(uint id, out nint window);
    [PreserveSig] public int SendControlMsg(uint id, uint message, nuint wParam, nint lParam, out nint result);
    [PreserveSig] public int QueryActiveShellView(out nint view);
    [PreserveSig] public int OnViewWindowActive(nint view);
    [PreserveSig] public int SetToolbarItems(nint buttons, uint count, uint flags);
}
