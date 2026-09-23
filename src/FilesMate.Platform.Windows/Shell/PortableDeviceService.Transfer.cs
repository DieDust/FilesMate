using System.Runtime.InteropServices;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Shell;

public sealed record DeviceFolderCapabilities(bool CanReceiveFiles);
public sealed record DeviceCopyResult(int Completed, int Skipped, bool Cancelled, int ErrorCode)
{
    public bool Succeeded => !Cancelled && ErrorCode >= 0 && Skipped == 0;
}

public static partial class PortableDeviceService
{
    private const uint CanCopy = 0x1, DropTarget = 0x100, ReadOnly = 0x40000, Folder = 0x20000000;
    private static readonly SemaphoreSlim TransferGate = new(1, 1);
    // Keep the Shell's progress, conflict and error UI. Never imply Yes to All,
    // replacement, or source deletion. Copy hooks must not add unselected companions.
    internal const uint CopyFlags = 0x2000 | 0x0200;

    public static Task<DeviceFolderCapabilities> GetFolderCapabilitiesAsync(PortableDeviceLocation location, CancellationToken token = default) => RunAsync(() =>
    {
        using var scope = new NativeScope();
        var item = ResolveShellItem(location.Uri, scope, token);
        item.GetAttributes(DropTarget | ReadOnly | Folder, out var attributes);
        return new DeviceFolderCapabilities(CanReceive(attributes));
    }, token);

    internal static bool CanReceive(uint attributes) => (attributes & (Folder | DropTarget | ReadOnly)) == (Folder | DropTarget);

    /// <summary>Copies through the device's Windows Shell provider, without staging whole files in memory or deleting sources.</summary>
    public static async Task<DeviceCopyResult> CopyAsync(IReadOnlyList<string> sources, string destination,
        nint owner = 0, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count is 0 or > 10000) throw new ArgumentException("Select between 1 and 10000 items.", nameof(sources));
        var snapshot = sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var source in snapshot) ValidateTransferLocation(source);
        ValidateTransferLocation(destination);
        // Await the actual native completion, including cancellation. Releasing an
        // application lifetime lease early could terminate a still-writing driver.
        await TransferGate.WaitAsync(token).ConfigureAwait(false);
        Task<DeviceCopyResult> pending;
        try
        {
            pending = ShellLocation.OnSta(() =>
            {
                try { return CopyOnSta(snapshot, destination, owner, token); }
                finally { TransferGate.Release(); }
            });
        }
        catch { TransferGate.Release(); throw; }
        return await pending.ConfigureAwait(false);
    }

    internal static void ValidateTransferLocation(string value)
    {
        if (PortableDeviceLocation.TryParse(value, out _)) return;
        if (string.IsNullOrWhiteSpace(value) || value.Length >= 32768 || value.Contains('\0')
            || value.StartsWith(PortableDeviceLocation.Prefix, StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\.\", StringComparison.Ordinal)
            || value.StartsWith(@"\\?\", StringComparison.Ordinal))
            throw new ArgumentException("A local path or a connected device item is required.");
    }

    private static DeviceCopyResult CopyOnSta(string[] sources, string destination, nint owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var scope = new NativeScope();
        var target = ResolveShellItem(destination, scope, token);
        target.GetAttributes(Folder | DropTarget | ReadOnly, out var attributes);
        if ((attributes & Folder) == 0) throw new DirectoryNotFoundException("Choose a destination folder.");
        if (PortableDeviceLocation.TryParse(destination, out _) && !CanReceive(attributes))
            throw new UnauthorizedAccessException("This device folder does not allow files to be added.");
        var operation = (ShellFileOperation.IFileOperation)scope.Own(Activator.CreateInstance(
            Type.GetTypeFromCLSID(ShellFileOperation.ClassId, throwOnError: true)!)!);
        operation.SetOwnerWindow(unchecked((uint)owner));
        operation.SetOperationFlags(CopyFlags);
        // Advise observes recursive children too. A successful folder creation
        // must not conceal a skipped or failed file inside that folder.
        var sink = new CopySink(token);
        operation.Advise(sink, out var cookie);
        try
        {
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                var item = ResolveShellItem(source, scope, token);
                item.GetAttributes(CanCopy, out var sourceAttributes);
                if ((sourceAttributes & CanCopy) == 0) throw new UnauthorizedAccessException("This item cannot be copied.");
                operation.CopyItem(item, target, null, null);
            }
            var hr = operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            var error = hr < 0 ? hr : sink.ErrorCode;
            var incomplete = sink.Incomplete;
            if (aborted || error < 0 || token.IsCancellationRequested || sink.Observed == 0)
                incomplete = Math.Max(incomplete, Math.Max(1, sources.Length - sink.Completed));
            return new(sink.Completed, incomplete,
                aborted || token.IsCancellationRequested || IsCopyCancelled(error), error);
        }
        finally { operation.Unadvise(cookie); GC.KeepAlive(sink); }
    }

    private static ShellFileOperation.IShellItem ResolveShellItem(string address, NativeScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!PortableDeviceLocation.TryParse(address, out var location))
        {
            ValidateTransferLocation(address);
            ShellFileOperation.SHCreateItemFromParsingName(Path.GetFullPath(address), 0, ShellFileOperation.ShellItemId, out var local);
            return (ShellFileOperation.IShellItem)scope.Own(local);
        }
        object shellObject;
        if (location.Parent is { } parent)
        {
            var folder = ResolveFolder(parent, token);
            shellObject = ResolveChild(folder, location.Segments[^1], token);
        }
        else
        {
            var folder = ResolveFolder(location, token);
            shellObject = folder.Self;
        }
        // Use the verified live FolderItem's PIDL. Device child parsing strings
        // alone are not reliable inputs to SHCreateItemFromParsingName.
        ShellFileOperation.SHGetIDListFromObject(shellObject, out var pidl);
        try
        {
            ShellFileOperation.SHCreateItemFromIDList(pidl, ShellFileOperation.ShellItemId, out var item);
            return (ShellFileOperation.IShellItem)scope.Own(item);
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    internal static bool IsCopyCompleted(int result, nint created) => created != 0 && result is 0 or 0x00270008 or 0x0027000A;
    internal static bool IsCopyCancelled(int result) => result is unchecked((int)0x800704C7) or unchecked((int)0x80270000) or unchecked((int)0x80270001);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class CopySink(CancellationToken token) : ShellFileOperation.IProgressSink
    {
        internal int Completed { get; private set; }
        internal int Incomplete { get; private set; }
        internal int Observed { get; private set; }
        internal int ErrorCode { get; private set; }
        private int CheckCancellation() => token.IsCancellationRequested ? unchecked((int)0x800704C7) : 0;
        public int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created)
        {
            Observed++;
            if (IsCopyCompleted(result, created)) Completed++;
            else if (result != 0x00270006) Incomplete++; // MERGE: observe child results, not an error or a finished copy.
            if (result < 0) ErrorCode = result;
            return CheckCancellation();
        }
        public int StartOperations() => CheckCancellation();
        public int FinishOperations(int result) { if (result < 0) ErrorCode = result; return 0; }
        public int PreCopyItem(uint flags, nint item, nint destination, nint name) => CheckCancellation();
        public int UpdateProgress(uint total, uint finished) => CheckCancellation();
        public int PreRenameItem(uint flags, nint item, nint name) => CheckCancellation();
        public int PostRenameItem(uint flags, nint item, nint name, int result, nint created) => 0;
        public int PreMoveItem(uint flags, nint item, nint destination, nint name) => CheckCancellation();
        public int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
        public int PreDeleteItem(uint flags, nint item) => CheckCancellation();
        public int PostDeleteItem(uint flags, nint item, int result, nint recycled) => 0;
        public int PreNewItem(uint flags, nint destination, nint name) => CheckCancellation();
        public int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
