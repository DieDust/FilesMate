using System.Runtime.InteropServices;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Operations;

internal static class WindowsRecycleOperation
{
    // Shell reports a completed recycle move as COPYENGINE_S_DONT_PROCESS_CHILDREN:
    // the item is already in the bin, so there is no recursive deletion to perform.
    internal static bool IsCompletedDeletion(int result) => result is 0 or 0x00270008;

    internal static void VerifyCompletion(int result, bool aborted, IEnumerable<bool> completed)
    {
        if (aborted || result == unchecked((int)0x800704C7) || result == unchecked((int)0x80270000)
            || result == unchecked((int)0x80270001)) throw new OperationCanceledException();
        Marshal.ThrowExceptionForHR(result);
        if (completed.Any(done => !done)) throw new IOException("Some items were not deleted.");
    }
    internal static void Run(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var operation = (ShellFileOperation.IFileOperation)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ShellFileOperation.ClassId, throwOnError: true)!)!;
        var items = new List<ShellFileOperation.IShellItem>();
        var sinks = new List<DeleteSink>();
        try
        {
            // Keep Windows' explicit warning when recycling would become permanent deletion.
            // Do not group an HTML file with unselected companion folders.
            operation.SetOperationFlags(0x00080000u | Shell32.FofNoConfirmation | Shell32.FofWantNukeWarning | 0x2000u);
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(path);
                ShellFileOperation.SHCreateItemFromParsingName(fullPath, 0, ShellFileOperation.ShellItemId, out var item);
                items.Add(item);
                var sink = new DeleteSink(fullPath, File.GetAttributes(fullPath), completed);
                sinks.Add(sink);
                operation.DeleteItem(item, sink);
            }
            var result = operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            // Callbacks are already delivered, including completed items before cancellation.
            VerifyCompletion(result, aborted, sinks.Select(sink => sink.Completed));
        }
        finally
        {
            Marshal.ReleaseComObject(operation);
            foreach (var item in items) Marshal.ReleaseComObject(item);
            GC.KeepAlive(sinks);
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DeleteSink(string path, FileAttributes originalAttributes, Action<RecycleItemResult>? completed) : ShellFileOperation.IProgressSink
    {
        internal bool Completed { get; private set; }
        public int PostDeleteItem(uint flags, nint item, int result, nint recycled)
        {
            // Skip/cancel HRESULTs can be nonnegative; neither is a completed deletion.
            if (!IsCompletedDeletion(result) || Path.Exists(path)) return 0;
            if (!Completed)
            {
                Completed = true;
                completed?.Invoke(new(path, recycled != 0) { Receipt = ReadReceipt(recycled) });
            }
            return 0;
        }

        private RecycleReceipt? ReadReceipt(nint recycled)
        {
            if (!OperatingSystem.IsWindows() || recycled == 0) return null;
            object? wrapper = null;
            nint text = 0;
            try
            {
                wrapper = Marshal.GetObjectForIUnknown(recycled);
                ((ShellFileOperation.IShellItem)wrapper).GetDisplayName(0x80058000u, out text); // SIGDN_FILESYSPATH
                var dataPath = Marshal.PtrToStringUni(text);
                if (dataPath is null || !Path.GetFileName(dataPath).StartsWith("$R", StringComparison.Ordinal)) return null;
                var infoPath = Path.Combine(Path.GetDirectoryName(dataPath)!, "$I" + Path.GetFileName(dataPath)[2..]);
                if (!RecycleBinRestore.TryReadOriginalPath(infoPath, out var original, out _)
                    || !string.Equals(original, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return null;
                return new(path, dataPath, infoPath, originalAttributes);
            }
            catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                // The deletion still completed. Missing proof must never become a name-only undo.
                return null;
            }
            finally
            {
                if (text != 0) Marshal.FreeCoTaskMem(text);
                if (wrapper is not null && Marshal.IsComObject(wrapper)) Marshal.ReleaseComObject(wrapper);
            }
        }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, nint item, nint name) => 0;
        public int PostRenameItem(uint flags, nint item, nint name, int result, nint created) => 0;
        public int PreMoveItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
        public int PreCopyItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
        public int PreDeleteItem(uint flags, nint item) => 0;
        public int PreNewItem(uint flags, nint destination, nint name) => 0;
        public int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created) => 0;
        public int UpdateProgress(uint total, uint finished) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
