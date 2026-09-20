using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Interop;

internal static partial class Shell32
{
    internal static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    internal static readonly Guid SidSTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    internal static readonly Guid IidIShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    internal static readonly Guid IidIDispatch = new("00020400-0000-0000-C000-000000000046");

    internal const int CsidlDesktop = 0;
    internal const int SwcDesktop = 0x08;
    internal const int SwfoNeedDispatch = 0x01;
    internal const uint SvgioBackground = 0;
}

/// <summary>Explorer's window registry; the desktop entry lets us reach the shell that runs outside our process.</summary>
[ComImport]
[Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IShellWindows
{
    [return: MarshalAs(UnmanagedType.IDispatch)]
    internal object FindWindowSW(
        [MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
        [MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
        int swClass,
        out int phwnd,
        int swfwOptions);
}

/// <summary>IServiceProvider; named apart from <see cref="System.IServiceProvider"/> to keep call sites unambiguous.</summary>
[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServiceProvider
{
    [PreserveSig]
    internal int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
}

[ComImport]
[Guid("000214E2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    // IOleWindow
    [PreserveSig] internal int GetWindow(out nint phwnd);
    [PreserveSig] internal int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    // IShellBrowser; only QueryActiveShellView is called, the rest keeps the vtable aligned.
    [PreserveSig] internal int InsertMenusSB(nint hmenuShared, nint lpMenuWidths);
    [PreserveSig] internal int SetMenuSB(nint hmenuShared, nint holemenuRes, nint hwndActiveObject);
    [PreserveSig] internal int RemoveMenusSB(nint hmenuShared);
    [PreserveSig] internal int SetStatusTextSB(nint pszStatusText);
    [PreserveSig] internal int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool fEnable);
    [PreserveSig] internal int TranslateAcceleratorSB(nint pmsg, ushort wID);
    [PreserveSig] internal int BrowseObject(nint pidl, uint wFlags);
    [PreserveSig] internal int GetViewStateStream(uint grfMode, out nint ppStrm);
    [PreserveSig] internal int GetControlWindow(uint id, out nint phwnd);
    [PreserveSig] internal int SendControlMsg(uint id, uint uMsg, nint wParam, nint lParam, out nint pret);
    [PreserveSig] internal int QueryActiveShellView(out IShellView ppshv);
    [PreserveSig] internal int OnViewWindowActive(IShellView pshv);
    [PreserveSig] internal int SetToolbarItems(nint lpButtons, uint nButtons, uint uFlags);
}

[ComImport]
[Guid("000214E3-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    // IOleWindow
    [PreserveSig] internal int GetWindow(out nint phwnd);
    [PreserveSig] internal int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    // IShellView; only GetItemObject is called, the rest keeps the vtable aligned.
    [PreserveSig] internal int TranslateAccelerator(nint pmsg);
    [PreserveSig] internal int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool fEnable);
    [PreserveSig] internal int UIActivate(uint uState);
    [PreserveSig] internal int Refresh();
    [PreserveSig] internal int CreateViewWindow(nint psvPrevious, nint pfs, nint psb, nint prcView, out nint phWnd);
    [PreserveSig] internal int DestroyViewWindow();
    [PreserveSig] internal int GetCurrentInfo(nint pfs);
    [PreserveSig] internal int AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam);
    [PreserveSig] internal int SaveViewState();
    [PreserveSig] internal int SelectItem(nint pidlItem, uint uFlags);
    [PreserveSig] internal int GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

[ComImport]
[Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IShellFolderViewDual
{
    internal object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
}

[ComImport]
[Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IShellDispatch2
{
    internal void ShellExecute(
        [MarshalAs(UnmanagedType.BStr)] string file,
        [MarshalAs(UnmanagedType.Struct)] object vArgs,
        [MarshalAs(UnmanagedType.Struct)] object vDir,
        [MarshalAs(UnmanagedType.Struct)] object vOperation,
        [MarshalAs(UnmanagedType.Struct)] object vShow);
}
