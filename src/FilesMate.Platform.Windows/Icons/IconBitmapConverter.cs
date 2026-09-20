using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Icons;

internal static class IconBitmapConverter
{
    public static IconBitmap? FromHicon(nint hIcon)
    {
        if (hIcon == 0)
        {
            return null;
        }

        try
        {
            if (!User32.GetIconInfo(hIcon, out var info))
            {
                return null;
            }

            try
            {
                var color = info.hbmColor != 0 ? info.hbmColor : info.hbmMask;
                if (color == 0)
                {
                    return null;
                }

                return FromBitmap(color);
            }
            finally
            {
                if (info.hbmColor != 0)
                {
                    Gdi32.DeleteObject(info.hbmColor);
                }

                if (info.hbmMask != 0)
                {
                    Gdi32.DeleteObject(info.hbmMask);
                }
            }
        }
        finally
        {
            User32.DestroyIcon(hIcon);
        }
    }

    private static IconBitmap? FromBitmap(nint hbm)
    {
        if (Gdi32.GetObjectW(hbm, System.Runtime.InteropServices.Marshal.SizeOf<BITMAP>(), out var bmp) == 0
            || bmp.bmWidth <= 0
            || bmp.bmHeight <= 0)
        {
            return null;
        }

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = bmp.bmWidth,
                biHeight = -bmp.bmHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            },
        };

        var pixels = new byte[checked(bmp.bmWidth * bmp.bmHeight * 4)];
        var hdc = User32.GetDC(0);
        if (hdc == 0)
        {
            return null;
        }

        try
        {
            if (Gdi32.GetDIBits(hdc, hbm, 0, (uint)bmp.bmHeight, pixels, ref info, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            _ = User32.ReleaseDC(0, hdc);
        }

        return new IconBitmap(bmp.bmWidth, bmp.bmHeight, pixels);
    }
}
