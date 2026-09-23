using System.Runtime.InteropServices;
using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Shell;

public static partial class PortableDeviceService
{
    private static readonly SemaphoreSlim ThumbnailGate = new(1, 1);

    public static Task<PortableDeviceEntry> GetDetailsAsync(PortableDeviceLocation location, CancellationToken token) => RunAsync(() =>
    {
        IShellFolderItem2 item;
        if (location.Parent is { } parent)
            item = ResolveChild(ResolveFolder(parent, token), location.Segments[^1], token);
        else item = (IShellFolderItem2)ResolveFolder(location, token).Self;
        var folder = (bool)item.IsFolder;
        long? size = null;
        DateTime? modified = null;
        try { object? value = item.ExtendedProperty("System.Size"); if (value is not null && !folder) size = Convert.ToInt64(value); }
        catch (Exception ex) when (ex is COMException or FormatException or InvalidCastException or OverflowException) { }
        try { object value = item.ModifyDate; if (value is DateTime time && time.Year > 1900) modified = time; }
        catch (COMException) { }
        return new PortableDeviceEntry((string)item.Name, location, folder, size, modified);
    }, token);

    /// <summary>Reads a bounded Shell thumbnail; never downloads a whole device file into an app cache.</summary>
    public static async Task<IconBitmap?> GetThumbnailAsync(PortableDeviceLocation location, int pixels, CancellationToken token)
    {
        await ThumbnailGate.WaitAsync(token).ConfigureAwait(false);
        Task<IconBitmap?> pending;
        try
        {
            pending = ShellLocation.OnSta<IconBitmap?>(() =>
            {
                try
                {
                    using var scope = new NativeScope();
                    var item = ResolveShellItem(location.Uri, scope, token);
                    var factory = (IShellItemImageFactory)item;
                    var size = new ImageSize { Width = Math.Clamp(pixels, 32, 512), Height = Math.Clamp(pixels, 32, 512) };
                    token.ThrowIfCancellationRequested();
                    var hr = factory.GetImage(size, 0x8, out var bitmap); // SIIGBF_THUMBNAILONLY
                    if (bitmap == 0) return null;
                    try { return hr >= 0 ? IconBitmapConverter.FromBitmap(bitmap) : null; }
                    finally { Gdi32.DeleteObject(bitmap); }
                }
                catch (Exception error) when (error is COMException or InvalidCastException or IOException) { return null; }
                finally { ThumbnailGate.Release(); }
            });
        }
        catch { ThumbnailGate.Release(); throw; }
        return await pending.WaitAsync(token).ConfigureAwait(false);
    }

    [StructLayout(LayoutKind.Sequential)] private struct ImageSize { public int Width; public int Height; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] public int GetImage(ImageSize size, uint flags, out nint bitmap);
    }
}
