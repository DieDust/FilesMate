using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Interop;

internal static partial class Shell32
{
    internal static readonly Guid IidIShellFolder = new("000214E6-0000-0000-C000-000000000046");
    internal static readonly Guid IidIContextMenu = new("000214E4-0000-0000-C000-000000000046");
    internal static readonly Guid IidIContextMenu2 = new("000214F4-0000-0000-C000-000000000046");
    internal static readonly Guid IidIContextMenu3 = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");

    internal const uint CmfExplore = 0x00000004;
    internal const uint CmfCanRename = 0x00000010;
    internal const uint CmfExtendedVerbs = 0x00000100;
    internal const uint CmfItemMenu = 0x00000080;

    internal const uint TpmLeftAlign = 0x0000;
    internal const uint TpmRightButton = 0x0002;
    internal const uint TpmReturnCmd = 0x0100;

    internal const uint SwShownormal = 1;

    internal const uint CmicMaskUnicode = 0x00004000;
    internal const uint CmicMaskPtInvoke = 0x20000000;

    internal const uint WmInitMenu = 0x0116;
    internal const uint WmInitMenuPopup = 0x0117;
    internal const uint WmDrawItem = 0x002B;
    internal const uint WmMeasureItem = 0x002C;
    internal const uint WmMenuChar = 0x0120;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    internal static extern int SHParseDisplayName(
        string pszName,
        nint pbc,
        out nint ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    internal static extern int SHBindToParent(
        nint pidl,
        ref Guid riid,
        out nint ppv,
        out nint ppidlLast);

    [DllImport("shell32.dll", PreserveSig = true)]
    internal static extern int SHGetDesktopFolder(out nint ppshf);

    [DllImport("shell32.dll")]
    internal static extern nint ILClone(nint pidl);

    [DllImport("shell32.dll")]
    internal static extern void ILFree(nint pidl);
}

internal static partial class User32
{
    internal const uint WsPopup = 0x80000000;
    internal const uint WsExToolwindow = 0x00000080;
    internal const uint WsExNoActivate = 0x08000000;
    internal const int ErrorClassAlreadyExists = 1410;

    internal delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    internal const uint MiimState = 0x00000001;
    internal const uint MiimId = 0x00000002;
    internal const uint MiimSubmenu = 0x00000004;
    internal const uint MiimString = 0x00000040;
    internal const uint MiimFtype = 0x00000100;
    internal const uint MftSeparator = 0x00000800;
    internal const uint MfsGrayed = 0x00000001;

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetMenuItemCount(nint hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMenuItemInfoW(nint hmenu, uint item, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFOW lpmii);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern uint TrackPopupMenuEx(
        nint hmenu,
        uint fuFlags,
        int x,
        int y,
        nint hwnd,
        nint lptpm);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hWnd, ref POINT pt);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(in WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);
}

internal static partial class Kernel32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint GetModuleHandleW(string? lpModuleName);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MENUITEMINFOW
{
    public uint cbSize;
    public uint fMask;
    public uint fType;
    public uint fState;
    public uint wID;
    public nint hSubMenu;
    public nint hbmpChecked;
    public nint hbmpUnchecked;
    public nuint dwItemData;
    public nint dwTypeData;
    public uint cch;
    public nint hbmpItem;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CMINVOKECOMMANDINFOEX
{
    public uint cbSize;
    public uint fMask;
    public nint hwnd;
    public nint lpVerb;
    public nint lpParameters;
    public nint lpDirectory;
    public int nShow;
    public uint dwHotKey;
    public nint hIcon;
    public nint lpTitle;
    public nint lpVerbW;
    public nint lpParametersW;
    public nint lpDirectoryW;
    public nint lpTitleW;
    public POINT ptInvoke;
}

[ComImport]
[Guid("000214E6-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig]
    internal int ParseDisplayName(
        nint hwnd,
        nint pbc,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        ref uint pchEaten,
        out nint ppidl,
        ref uint pdwAttributes);

    [PreserveSig]
    internal int EnumObjects(nint hwnd, uint grfFlags, out nint ppenumIDList);

    [PreserveSig]
    internal int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);

    [PreserveSig]
    internal int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);

    [PreserveSig]
    internal int CompareIDs(nint lParam, nint pidl1, nint pidl2);

    [PreserveSig]
    internal int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);

    [PreserveSig]
    internal int GetAttributesOf(
        uint cidl,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] apidl,
        ref uint rgfInOut);

    [PreserveSig]
    internal int GetUIObjectOf(
        nint hwndOwner,
        uint cidl,
        // IShellFolder expects a C array of PIDL pointers, not COM's default SAFEARRAY.
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] apidl,
        ref Guid riid,
        nint rgfReserved,
        out nint ppv);

    [PreserveSig]
    internal int GetDisplayNameOf(nint pidl, uint uFlags, nint pName);

    [PreserveSig]
    internal int SetNameOf(
        nint hwnd,
        nint pidl,
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        uint uFlags,
        out nint ppidlOut);
}

[ComImport]
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig]
    internal int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    internal int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

    [PreserveSig]
    internal int GetCommandString(nuint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);
}

[ComImport]
[Guid("000214F4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2
{
    [PreserveSig]
    internal int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    internal int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

    [PreserveSig]
    internal int GetCommandString(nuint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);

    [PreserveSig]
    internal int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
}

[ComImport]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3
{
    [PreserveSig]
    internal int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    internal int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

    [PreserveSig]
    internal int GetCommandString(nuint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);

    [PreserveSig]
    internal int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);

    [PreserveSig]
    internal int HandleMenuMsg2(uint uMsg, nint wParam, nint lParam, out nint plResult);
}
