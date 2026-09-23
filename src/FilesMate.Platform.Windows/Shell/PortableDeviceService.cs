using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Shell;

/// <summary>Shell objects never cross the worker apartment.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static partial class PortableDeviceService
{
    // A slow/disconnected driver cannot cause an unbounded number of blocked STA threads.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static Task<IReadOnlyList<PortableDeviceLocation>> GetDevicesAsync(CancellationToken token = default) => RunAsync(() =>
    {
        var shell = (IShellDispatch)Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
        var computer = (IShellFolder2)shell.NameSpace(17);
        var items = (IShellFolderItems)computer.Items();
        var result = new List<PortableDeviceLocation>();
        for (var index = 0; index < (int)items.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var item = (IShellFolderItem2)items.Item(index);
            var path = (string)item.Path;
            if (PortableDeviceLocation.IsDeviceRoot(path) && (bool)item.IsFolder && !(bool)item.IsFileSystem)
                result.Add(new(path, (string)item.Name, []));
        }
        return (IReadOnlyList<PortableDeviceLocation>)result;
    }, token);

    public static Task<IReadOnlyList<PortableDeviceEntry>> ReadFolderAsync(PortableDeviceLocation location, CancellationToken token) => RunAsync(() =>
    {
        var folder = ResolveFolder(location, token);
        var items = (IShellFolderItems)folder.Items();
        var result = new List<PortableDeviceEntry>();
        for (var index = 0; index < (int)items.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var item = (IShellFolderItem2)items.Item(index);
            var name = (string)item.Name;
            var child = location.Child(name, (string)item.Path);
            var isFolder = (bool)item.IsFolder;
            long? size = null;
            DateTime? modified = null;
            if (!isFolder)
            {
                try { object? value = item.ExtendedProperty("System.Size"); if (value is not null) size = Convert.ToInt64(value); }
                catch (Exception error) when (error is COMException or FormatException or InvalidCastException or OverflowException) { }
                try { object value = item.ModifyDate; if (value is DateTime time && time.Year > 1900) modified = time; }
                catch (COMException) { }
            }
            result.Add(new(name, child, isFolder, size, modified));
        }
        return (IReadOnlyList<PortableDeviceEntry>)result.OrderByDescending(e => e.IsFolder).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, token);

    public static Task OpenFileAsync(PortableDeviceLocation location, CancellationToken token) => RunAsync(() =>
    {
        var parent = location.Parent ?? throw new ArgumentException("Select a file.");
        var folder = ResolveFolder(parent, token);
        var item = ResolveChild(folder, location.Segments[^1], token);
        if ((bool)item.IsFolder) throw new ArgumentException("Select a file.");
        token.ThrowIfCancellationRequested();
        item.InvokeVerb("open");
        return true;
    }, token);

    private static IShellFolder2 ResolveFolder(PortableDeviceLocation location, CancellationToken token)
    {
        if (!PortableDeviceLocation.TryParse(location.Uri, out _)) throw new ArgumentException("Invalid device location.");
        var shell = (IShellDispatch)Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
        var computer = (IShellFolder2)shell.NameSpace(17);
        // Resolve only an actual connected portable device under This PC. Never
        // activate an arbitrary Shell parsing name supplied in a saved tab URI.
        var root = ResolveChild(computer, new PortableDeviceSegment(location.RootName, location.Root), token);
        if (!(bool)root.IsFolder || (bool)root.IsFileSystem) throw new DirectoryNotFoundException("Device unavailable.");
        var folder = (IShellFolder2)root.GetFolder;
        // WPD child parsing names cannot reliably be passed to Shell.NameSpace.
        // Walk FolderItem.GetFolder, verifying each opaque identity before entering it.
        foreach (var segment in location.Segments)
        {
            token.ThrowIfCancellationRequested();
            var item = ResolveChild(folder, segment, token);
            if (!(bool)item.IsFolder) throw new DirectoryNotFoundException("Device folder unavailable.");
            folder = (IShellFolder2)item.GetFolder;
        }
        return folder;
    }

    private static IShellFolderItem2 ResolveChild(IShellFolder2 folder, PortableDeviceSegment segment, CancellationToken token)
    {
        object? candidate = null;
        // ParseName may accept absolute paths. Only use its fast path for a
        // plain child name; otherwise resolve the exact identity by enumeration.
        try { if (segment.Name.IndexOfAny(['\\', '/', ':']) < 0 && segment.Name is not "." and not "..") candidate = folder.ParseName(segment.Name); }
        catch (COMException) { /* Some device providers only support enumeration. */ }
        if (candidate is not null)
        {
            var parsed = (IShellFolderItem2)candidate;
            if (string.Equals((string)parsed.Path, segment.Path, StringComparison.OrdinalIgnoreCase)) return parsed;
        }
        var items = (IShellFolderItems)folder.Items();
        for (var index = 0; index < (int)items.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var item = (IShellFolderItem2)items.Item(index);
            if (string.Equals(item.Path, segment.Path, StringComparison.OrdinalIgnoreCase)) return (IShellFolderItem2)item;
        }
        throw new DirectoryNotFoundException("Device item unavailable.");
    }

    private static async Task<T> RunAsync<T>(Func<T> work, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        Task<T> pending;
        try { pending = ShellLocation.OnSta(() =>
        {
            try { token.ThrowIfCancellationRequested(); return work(); }
            finally { Gate.Release(); }
        }); }
        catch { Gate.Release(); throw; }
        // Cancel the caller promptly. The one native operation retains the gate
        // until the driver returns. No native work is moved onto the UI thread.
        return await pending.WaitAsync(token).ConfigureAwait(false);
    }

    private sealed class NativeScope : IDisposable
    {
        private readonly List<object> _privateObjects = [];
        // Only fresh, exclusively owned native operations and Shell items created
        // by SHCreateItem* are released here. They never leave this worker.
        public object Own(object value) { _privateObjects.Add(value); return value; }
        public void Dispose()
        {
            for (var index = _privateObjects.Count - 1; index >= 0; index--)
                if (Marshal.IsComObject(_privateObjects[index])) Marshal.ReleaseComObject(_privateObjects[index]);
            _privateObjects.Clear();
        }
    }
}
