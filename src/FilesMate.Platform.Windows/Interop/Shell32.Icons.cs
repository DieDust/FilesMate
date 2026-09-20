using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Interop;

internal static partial class Shell32
{
    internal const uint ShgfiIcon = 0x000000100;
    internal const uint ShgfiSmallIcon = 0x000000001;
    internal const uint ShgfiLargeIcon = 0x000000000;
    internal const uint ShgfiUseFileAttributes = 0x000000010;
    internal const uint ShgfiAddOverlays = 0x000000020;

    internal const uint FileAttributeNormal = 0x80;
    internal const uint FileAttributeDirectory = 0x10;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint SHGetFileInfoW(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFOW psfi,
        uint cbFileInfo,
        uint uFlags);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEINFOW
{
    public nint hIcon;
    public int iIcon;
    public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string szTypeName;
}

internal static partial class User32
{
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int ReleaseDC(nint hWnd, nint hDC);
}

internal static partial class Gdi32
{
    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern int GetObjectW(nint hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern int GetDIBits(
        nint hdc,
        nint hbm,
        uint start,
        uint cLines,
        byte[] lpvBits,
        ref BITMAPINFO lpbmi,
        uint usage);
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    [MarshalAs(UnmanagedType.Bool)]
    public bool fIcon;
    public int xHotspot;
    public int yHotspot;
    public nint hbmMask;
    public nint hbmColor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType;
    public int bmWidth;
    public int bmHeight;
    public int bmWidthBytes;
    public ushort bmPlanes;
    public ushort bmBitsPixel;
    public nint bmBits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    public uint bmiColors;
}
