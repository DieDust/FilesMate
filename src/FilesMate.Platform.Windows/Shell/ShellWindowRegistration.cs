using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FilesMate.Platform.Windows.Shell;

/// <summary>Receives the second, COM-based selection phase of SHOpenFolderAndSelectItems.</summary>
[SupportedOSPlatform("windows")]
public sealed class ShellWindowRegistration : IDisposable
{
    private IShellWindowRegistry? _registry;
    private ShellSelectionDocument? _document;
    private ShellBrowserAutomation? _browser;
    private int? _cookie;
    private string? _folder;

    public void Navigate(nint window, string? folder, Action<string> select,
        nint viewWindow = 0, Func<IReadOnlyList<string>>? selectedPaths = null)
    {
        if (string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase)) return;
        Dispose();
        if (window == 0 || string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder)) return;
        var pidl = ILCreateFromPath(folder);
        if (pidl == 0) return;
        try
        {
            var size = checked((int)ILGetSize(pidl));
            if (size < 2) return;
            var bytes = new byte[size];
            Marshal.Copy(pidl, bytes, 0, size);
            object location = bytes;
            object root = null!;
            _registry = (IShellWindowRegistry)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!)!;
            _document = new ShellSelectionDocument(window, folder, select, viewWindow, selectedPaths);
            _browser = new ShellBrowserAutomation(window, _document);
            // SWC_BROWSER is what the shell searches for. RegisterPending is required
            // even when the HWND exists: it completes an outstanding shell launch.
            _registry.RegisterPending(unchecked((int)GetCurrentThreadId()), ref location, ref root, 1, out var pending);
            _cookie = pending;
            _registry.Register(_browser, unchecked((int)window), 1, out var registered);
            if (pending != registered) _registry.Revoke(pending);
            _cookie = registered;
            _registry.OnNavigate(registered, ref location);
            _folder = folder;
        }
        catch
        {
            Dispose();
            throw;
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    public void Dispose()
    {
        _document?.Disconnect();
        if (_registry is not null)
        {
            try { if (_cookie is { } cookie) _registry.Revoke(cookie); }
            catch (COMException) { /* Explorer may have restarted. */ }
            Marshal.ReleaseComObject(_registry);
        }
        _registry = null;
        _document = null;
        _browser = null;
        _cookie = null;
        _folder = null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint ILCreateFromPath(string path);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}

[SupportedOSPlatform("windows"), ComVisible(true), ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed partial class ShellSelectionDocument(nint window, string folder, Action<string> select,
    nint viewWindow = 0, Func<IReadOnlyList<string>>? selectedPaths = null)
    : IShellServiceProvider, ISelectionShellView, ISelectionFolderView2
{
    private const int NotImplemented = unchecked((int)0x80004001);
    private Action<string>? _select = select;
    private Func<IReadOnlyList<string>>? _selectedPaths = selectedPaths;
    internal bool CanExportSelection => _selectedPaths is not null;
    public void Disconnect() { _select = null; _selectedPaths = null; }

    public int QueryService(ref Guid service, ref Guid iid, out nint result)
    {
        result = 0;
        if (service != new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")) return unchecked((int)0x80004002);
        var unknown = Marshal.GetIUnknownForObject(this);
        try { return Marshal.QueryInterface(unknown, in iid, out result); }
        finally { Marshal.Release(unknown); }
    }

    public int SelectItem(nint child, uint flags)
    {
        if (_select is null) return unchecked((int)0x80004004);
        if (child == 0 || (flags & 1) == 0) return 0;
        var parent = ILCreateFromPath(folder);
        if (parent == 0) return unchecked((int)0x80070002);
        nint full = 0;
        try
        {
            full = ILCombine(parent, child);
            if (full == 0) return unchecked((int)0x8007000E);
            var hr = SHGetNameFromIDList(full, 0x80058000, out var name);
            if (hr < 0) return hr;
            try
            {
                var path = Marshal.PtrToStringUni(name);
                if (!string.IsNullOrEmpty(path)) _select(path);
            }
            finally { Marshal.FreeCoTaskMem(name); }
            return 0;
        }
        catch (Exception error) { return Marshal.GetHRForException(error); }
        finally
        {
            if (full != 0) Marshal.FreeCoTaskMem(full);
            Marshal.FreeCoTaskMem(parent);
        }
    }

    public int GetWindow(out nint value) { value = viewWindow != 0 ? viewWindow : window; return _select is null ? unchecked((int)0x80004004) : 0; }
    public int ContextSensitiveHelp(int enter) => NotImplemented;
    public int TranslateAccelerator(nint message) => 1;
    public int EnableModeless(int enable) => NotImplemented;
    public int UIActivate(uint state) => 0;
    public int Refresh() => NotImplemented;
    public int CreateViewWindow(nint previous, nint settings, nint browser, nint bounds, out nint value) { value = 0; return NotImplemented; }
    public int DestroyViewWindow() => NotImplemented;
    public int GetCurrentInfo(nint settings) => NotImplemented;
    public int AddPropertySheetPages(uint reserved, nint callback, nint parameter) => NotImplemented;
    public int SaveViewState() => NotImplemented;
    public int GetItemObject(uint item, ref Guid iid, out nint result) { result = 0; return NotImplemented; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint ILCreateFromPath(string path);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(nint pidl, uint kind, out nint name);
}

[SupportedOSPlatform("windows"), ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ShellBrowserAutomation(nint window, ShellSelectionDocument document) : IShellBrowserAutomation, IShellServiceProvider
{
    private const int NotImplemented = unchecked((int)0x80004001);
    private readonly SelectionShellBrowser _shellBrowser = new(window, document);
    public int QueryService(ref Guid service, ref Guid iid, out nint result)
    {
        result = 0;
        if (!document.CanExportSelection || service != new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"))
            return unchecked((int)0x80004002);
        var unknown = Marshal.GetIUnknownForObject(_shellBrowser);
        try { return Marshal.QueryInterface(unknown, in iid, out result); }
        finally { Marshal.Release(unknown); }
    }
    public int GoBack() { return NotImplemented; }
    public int GoForward() { return NotImplemented; }
    public int GoHome() { return NotImplemented; }
    public int GoSearch() { return NotImplemented; }
    public int Navigate(string url, nint flags, nint target, nint data, nint headers) { return NotImplemented; }
    public int Refresh() { return NotImplemented; }
    public int Refresh2(nint level) { return NotImplemented; }
    public int Stop() { return NotImplemented; }
    public int get_Application(out nint value) { value = 0; return NotImplemented; }
    public int get_Parent(out nint value) { value = 0; return NotImplemented; }
    public int get_Container(out nint value) { value = 0; return NotImplemented; }
    public int get_Document(out nint value) { value = Marshal.GetIDispatchForObject(document); return 0; }
    public int get_TopLevelContainer(out short value) { value = 0; return NotImplemented; }
    public int get_Type(out nint value) { value = 0; return NotImplemented; }
    public int get_Left(out int value) { value = 0; return NotImplemented; }
    public int put_Left(int value) { return NotImplemented; }
    public int get_Top(out int value) { value = 0; return NotImplemented; }
    public int put_Top(int value) { return NotImplemented; }
    public int get_Width(out int value) { value = 0; return NotImplemented; }
    public int put_Width(int value) { return NotImplemented; }
    public int get_Height(out int value) { value = 0; return NotImplemented; }
    public int put_Height(int value) { return NotImplemented; }
    public int get_LocationName(out nint value) { value = 0; return NotImplemented; }
    public int get_LocationURL(out nint value) { value = 0; return NotImplemented; }
    public int get_Busy(out short value) { value = 0; return NotImplemented; }
    public int Quit() { return NotImplemented; }
    public int ClientToWindow(ref int width, ref int height) { return NotImplemented; }
    public int PutProperty(string name, object value) { return NotImplemented; }
    public int GetProperty(string name, out object value) { value = null!; return NotImplemented; }
    public int get_Name(out nint value) { value = 0; return NotImplemented; }
    public int get_HWND(out nint value) { value = window; return 0; }
    public int get_FullName(out nint value) { value = 0; return NotImplemented; }
    public int get_Path(out nint value) { value = 0; return NotImplemented; }
    public int get_Visible(out short value) { value = 0; return NotImplemented; }
    public int put_Visible(short value) { return NotImplemented; }
    public int get_StatusBar(out short value) { value = 0; return NotImplemented; }
    public int put_StatusBar(short value) { return NotImplemented; }
    public int get_StatusText(out nint value) { value = 0; return NotImplemented; }
    public int put_StatusText(string value) { return NotImplemented; }
    public int get_ToolBar(out int value) { value = 0; return NotImplemented; }
    public int put_ToolBar(int value) { return NotImplemented; }
    public int get_MenuBar(out short value) { value = 0; return NotImplemented; }
    public int put_MenuBar(short value) { return NotImplemented; }
    public int get_FullScreen(out short value) { value = 0; return NotImplemented; }
    public int put_FullScreen(short value) { return NotImplemented; }
}
