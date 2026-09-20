using System.Runtime.InteropServices;

// The declarations follow the Windows SDK ExDisp.h and ShObjIdl_core.h vtable order.
namespace FilesMate.Platform.Windows.Shell;

[ComVisible(true), Guid("0002DF05-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IShellBrowserAutomation
{
    [PreserveSig] public int GoBack();
    [PreserveSig] public int GoForward();
    [PreserveSig] public int GoHome();
    [PreserveSig] public int GoSearch();
    [PreserveSig] public int Navigate([MarshalAs(UnmanagedType.BStr)] string url, nint flags, nint target, nint data, nint headers);
    [PreserveSig] public int Refresh();
    [PreserveSig] public int Refresh2(nint level);
    [PreserveSig] public int Stop();
    [PreserveSig] public int get_Application(out nint value);
    [PreserveSig] public int get_Parent(out nint value);
    [PreserveSig] public int get_Container(out nint value);
    [PreserveSig] public int get_Document(out nint value);
    [PreserveSig] public int get_TopLevelContainer(out short value);
    [PreserveSig] public int get_Type(out nint value);
    [PreserveSig] public int get_Left(out int value);
    [PreserveSig] public int put_Left(int value);
    [PreserveSig] public int get_Top(out int value);
    [PreserveSig] public int put_Top(int value);
    [PreserveSig] public int get_Width(out int value);
    [PreserveSig] public int put_Width(int value);
    [PreserveSig] public int get_Height(out int value);
    [PreserveSig] public int put_Height(int value);
    [PreserveSig] public int get_LocationName(out nint value);
    [PreserveSig] public int get_LocationURL(out nint value);
    [PreserveSig] public int get_Busy(out short value);
    [PreserveSig] public int Quit();
    [PreserveSig] public int ClientToWindow(ref int width, ref int height);
    [PreserveSig] public int PutProperty([MarshalAs(UnmanagedType.BStr)] string name, [MarshalAs(UnmanagedType.Struct)] object value);
    [PreserveSig] public int GetProperty([MarshalAs(UnmanagedType.BStr)] string name, out object value);
    [PreserveSig] public int get_Name(out nint value);
    [PreserveSig] public int get_HWND(out nint value);
    [PreserveSig] public int get_FullName(out nint value);
    [PreserveSig] public int get_Path(out nint value);
    [PreserveSig] public int get_Visible(out short value);
    [PreserveSig] public int put_Visible(short value);
    [PreserveSig] public int get_StatusBar(out short value);
    [PreserveSig] public int put_StatusBar(short value);
    [PreserveSig] public int get_StatusText(out nint value);
    [PreserveSig] public int put_StatusText([MarshalAs(UnmanagedType.BStr)] string value);
    [PreserveSig] public int get_ToolBar(out int value);
    [PreserveSig] public int put_ToolBar(int value);
    [PreserveSig] public int get_MenuBar(out short value);
    [PreserveSig] public int put_MenuBar(short value);
    [PreserveSig] public int get_FullScreen(out short value);
    [PreserveSig] public int put_FullScreen(short value);
}

[ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IShellWindowRegistry
{
    public int Count { get; }
    [return: MarshalAs(UnmanagedType.IDispatch)] public object Item([MarshalAs(UnmanagedType.Struct)] object index);
    [return: MarshalAs(UnmanagedType.IUnknown)] public object NewEnum();
    public void Register([MarshalAs(UnmanagedType.IDispatch)] object browser, int hwnd, int windowClass, out int cookie);
    public void RegisterPending(int thread, ref object location, ref object root, int windowClass, out int cookie);
    public void Revoke(int cookie);
    public void OnNavigate(int cookie, ref object location);
    public void OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
}

[ComVisible(true), Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellServiceProvider
{
    [PreserveSig] public int QueryService(ref Guid service, ref Guid iid, out nint result);
}

[ComVisible(true), Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISelectionShellView
{
    [PreserveSig] public int GetWindow(out nint window);
    [PreserveSig] public int ContextSensitiveHelp(int enter);
    [PreserveSig] public int TranslateAccelerator(nint message);
    [PreserveSig] public int EnableModeless(int enable);
    [PreserveSig] public int UIActivate(uint state);
    [PreserveSig] public int Refresh();
    [PreserveSig] public int CreateViewWindow(nint previous, nint settings, nint browser, nint bounds, out nint window);
    [PreserveSig] public int DestroyViewWindow();
    [PreserveSig] public int GetCurrentInfo(nint settings);
    [PreserveSig] public int AddPropertySheetPages(uint reserved, nint callback, nint parameter);
    [PreserveSig] public int SaveViewState();
    [PreserveSig] public int SelectItem(nint child, uint flags);
    [PreserveSig] public int GetItemObject(uint item, ref Guid iid, out nint result);
}
