using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace FilesMate.Platform.Windows.Shell;

public sealed partial class ShellSelectionDocument
{
    public int GetCurrentViewMode(out uint mode) { mode = 0; return NotImplemented; }
    public int SetCurrentViewMode(uint mode) {  return NotImplemented; }
    public int GetFolder(ref Guid iid, out nint result) { result = 0; return NotImplemented; }
    public int Item(int index, out nint item) { item = 0; return NotImplemented; }
    public int GetSelectionMarkedItem(out int index) { index = 0; return NotImplemented; }
    public int GetFocusedItem(out int index) { index = 0; return NotImplemented; }
    public int GetItemPosition(nint item, nint point) {  return NotImplemented; }
    public int GetSpacing(nint point) {  return NotImplemented; }
    public int GetDefaultSpacing(nint point) {  return NotImplemented; }
    public int GetAutoArrange() {  return NotImplemented; }
    public int SelectItem(int index, uint flags) {  return NotImplemented; }
    public int SelectAndPositionItems(uint count, nint items, nint points, uint flags) {  return NotImplemented; }
    public int SetGroupBy(nint key, int ascending) {  return NotImplemented; }
    public int GetGroupBy(nint key, nint ascending) {  return NotImplemented; }
    public int SetViewProperty(nint item, nint key, nint value) {  return NotImplemented; }
    public int GetViewProperty(nint item, nint key, nint value) {  return NotImplemented; }
    public int SetTileViewProperties(nint item, nint text) {  return NotImplemented; }
    public int SetExtendedTileViewProperties(nint item, nint text) {  return NotImplemented; }
    public int SetText(int type, nint text) {  return NotImplemented; }
    public int SetCurrentFolderFlags(uint mask, uint flags) {  return NotImplemented; }
    public int GetCurrentFolderFlags(out uint flags) { flags = 0; return NotImplemented; }
    public int GetSortColumnCount(out int count) { count = 0; return NotImplemented; }
    public int SetSortColumns(nint columns, int count) {  return NotImplemented; }
    public int GetSortColumns(nint columns, int count) {  return NotImplemented; }
    public int GetItem(int index, ref Guid iid, out nint result) { result = 0; return NotImplemented; }
    public int GetVisibleItem(int start, int previous, out int index) { index = 0; return NotImplemented; }
    public int GetSelectedItem(int start, out int index) { index = 0; return NotImplemented; }
    public int GetSelectionState(nint item, out uint flags) { flags = 0; return NotImplemented; }
    public int InvokeVerbOnSelection(nint verb) {  return NotImplemented; }
    public int SetViewModeAndIconSize(int mode, int size) {  return NotImplemented; }
    public int GetViewModeAndIconSize(out int mode, out int size) { mode = 0; size = 0; return NotImplemented; }
    public int SetGroupSubsetCount(uint rows) {  return NotImplemented; }
    public int GetGroupSubsetCount(out uint rows) { rows = 0; return NotImplemented; }
    public int SetRedraw(int redraw) {  return NotImplemented; }
    public int IsMoveInSameFolder() {  return NotImplemented; }
    public int DoRename() {  return NotImplemented; }

    public int ItemCount(uint flags, out int count)
    {
        count = 0;
        if (_selectedPaths is null) return unchecked((int)0x80004004);
        if ((flags & 0xF) != 1) return NotImplemented;
        try { count = _selectedPaths().Count; return 0; }
        catch (Exception error) { return Marshal.GetHRForException(error); }
    }

    public int GetSelection(int noneImpliesFolder, out nint result)
    {
        var iid = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
        return Items(1, ref iid, out result);
    }

    public int Items(uint flags, ref Guid iid, out nint result)
    {
        result = 0;
        if (_selectedPaths is null) return unchecked((int)0x80004004);
        if ((flags & 0xF) != 1) return NotImplemented;
        var identifiers = new List<nint>();
        nint array = 0;
        try
        {
            // Materialize only upon an external request. No directory enumeration,
            // thumbnail work, clipboard mutation, or selection polling.
            foreach (var path in _selectedPaths())
            {
                var identifier = ILCreateFromPath(path);
                if (identifier == 0) return unchecked((int)0x80070002);
                identifiers.Add(identifier);
            }
            if (identifiers.Count == 0) return unchecked((int)0x80070490);
            var hr = SHCreateShellItemArrayFromIDLists((uint)identifiers.Count, identifiers.ToArray(), out array);
            return hr < 0 ? hr : Marshal.QueryInterface(array, in iid, out result);
        }
        catch (Exception error) { return Marshal.GetHRForException(error); }
        finally
        {
            if (array != 0) Marshal.Release(array);
            foreach (var identifier in identifiers) Marshal.FreeCoTaskMem(identifier);
        }
    }

    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] identifiers, out nint result);
}

[SupportedOSPlatform("windows"), ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class SelectionShellBrowser(nint handle, ShellSelectionDocument document) : ISelectionShellBrowser
{
    private const int NotImplemented = unchecked((int)0x80004001);
    public int ContextSensitiveHelp(int enter) {  return NotImplemented; }
    public int InsertMenusSB(nint menu, nint widths) {  return NotImplemented; }
    public int SetMenuSB(nint menu, nint hole, nint active) {  return NotImplemented; }
    public int RemoveMenusSB(nint menu) {  return NotImplemented; }
    public int SetStatusTextSB(nint text) {  return NotImplemented; }
    public int EnableModelessSB(int enable) {  return NotImplemented; }
    public int TranslateAcceleratorSB(nint message, ushort id) {  return NotImplemented; }
    public int BrowseObject(nint item, uint flags) {  return NotImplemented; }
    public int GetViewStateStream(uint mode, out nint stream) { stream = 0; return NotImplemented; }
    public int GetControlWindow(uint id, out nint window) { window = 0; return NotImplemented; }
    public int SendControlMsg(uint id, uint message, nuint wParam, nint lParam, out nint result) { result = 0; return NotImplemented; }
    public int OnViewWindowActive(nint view) {  return NotImplemented; }
    public int SetToolbarItems(nint buttons, uint count, uint flags) {  return NotImplemented; }
    public int GetWindow(out nint window) { window = handle; return 0; }
    public int QueryActiveShellView(out nint view)
    {
        view = 0;
        if (!document.CanExportSelection) return unchecked((int)0x80004004);
        var iid = typeof(ISelectionShellView).GUID;
        var unknown = Marshal.GetIUnknownForObject(document);
        try { return Marshal.QueryInterface(unknown, in iid, out view); }
        finally { Marshal.Release(unknown); }
    }
}
