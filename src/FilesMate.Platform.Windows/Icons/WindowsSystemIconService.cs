using System.IO;
using System.Runtime.InteropServices;

using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Icons;

/// <summary>
/// Shell icon extraction with extension coalescing. Native handles are converted once and destroyed.
/// </summary>
public sealed class WindowsSystemIconService : IIconService
{
    private readonly IconLoadCache _cache = new(16L * 1024 * 1024, concurrency: 4);

    public IconBitmap? TryGetCached(in IconKey key) =>
        _cache.TryGetCached(key.CacheId);

    public void ClearCache() => _cache.Clear();

    public Task<IconBitmap?> GetAsync(
        IconKey key,
        string? path,
        FileAttributes attributes,
        bool directory,
        CancellationToken cancellationToken)
        => _cache.GetAsync(key.CacheId,
            token => Task.Run(() => Extract(key, path, attributes, directory), token),
            cancellationToken);

    private static IconBitmap? Extract(IconKey key, string? path, FileAttributes attributes, bool directory)
    {
        // Thread-pool threads are not necessarily COM-initialized. Resolving
        // shortcut identities through SHGetFileInfo requires a COM apartment.
        var initialized = CoInitializeEx(0, 0);
        try { return ExtractInitialized(key, path, attributes, directory); }
        finally { if (initialized >= 0) CoUninitialize(); }
    }

    private static IconBitmap? ExtractInitialized(IconKey key, string? path, FileAttributes attributes, bool directory)
    {
        var info = new SHFILEINFOW();
        uint flags = Shell32.ShgfiIcon | (key.PixelSize <= 20 ? Shell32.ShgfiSmallIcon : Shell32.ShgfiLargeIcon);
        string pszPath;
        uint fileAttributes;

        if (key.Identity == "dir")
        {
            pszPath = "folder";
            fileAttributes = Shell32.FileAttributeDirectory;
            flags |= Shell32.ShgfiUseFileAttributes;
        }
        else if (key.Identity.StartsWith("e:", StringComparison.Ordinal))
        {
            var extension = key.Identity[2..];
            pszPath = extension == "." ? "file" : "file" + extension;
            fileAttributes = Shell32.FileAttributeNormal;
            flags |= Shell32.ShgfiUseFileAttributes;
        }
        else
        {
            pszPath = path ?? "file";
            fileAttributes = (uint)attributes;
            if (directory)
            {
                fileAttributes |= Shell32.FileAttributeDirectory;
            }

            flags |= Shell32.ShgfiAddOverlays;
        }

        var result = Shell32.SHGetFileInfoW(
            pszPath,
            fileAttributes,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFOW>(),
            flags);
        if (result == 0 || info.hIcon == 0)
        {
            return null;
        }

        return IconBitmapConverter.FromHicon(info.hIcon);
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
