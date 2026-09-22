using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Operations;

public sealed record FileCopyProgress(string Source, string Destination, long Transferred, long Total);

internal static class WindowsFileCopy
{
    // Called on the transfer worker. CopyFileEx preserves alternate streams and metadata.
    public static void Copy(string source, string staging, string destination, CancellationToken token,
        IProgress<FileCopyProgress>? progress = null, Action<nint>? validateSource = null)
    {
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var reported = false;
        Exception? progressError = null;
        var sourceValidated = validateSource is null;
        uint Report(long total, long transferred, long streamSize, long streamTransferred,
            uint streamNumber, uint reason, nint sourceHandle, nint targetHandle, nint data)
        {
            // STOP leaves the owned staging file for cleanup after its reservation closes.
            if (token.IsCancellationRequested) return 2;
            if (!sourceValidated)
            {
                try { validateSource!(sourceHandle); sourceValidated = true; }
                catch (Exception error) { progressError = error; return 2; }
            }
            if (progress is not null && ((!reported && transferred > 0) || watch.ElapsedMilliseconds >= 100 || transferred == total))
            {
                try { progress.Report(new(source, destination, transferred, total)); }
                catch (Exception error) { progressError = error; return 2; }
                watch.Restart();
                reported = true;
            }
            return token.IsCancellationRequested ? 2u : 0u;
        }
        var success = CopyFileExW(source, staging, Report, 0, 0, 0);
        var errorCode = Marshal.GetLastWin32Error();
        token.ThrowIfCancellationRequested();
        if (progressError is not null) throw new IOException("Copy progress failed.", progressError);
        if (!success) throw new IOException(new Win32Exception(errorCode).Message, unchecked((int)(0x80070000u | (uint)errorCode)));
        if (!sourceValidated) throw new IOException("The copied source could not be verified.");
    }

    private delegate uint CopyProgress(long total, long transferred, long streamSize, long streamTransferred,
        uint streamNumber, uint reason, nint source, nint destination, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CopyFileExW(string source, string destination, CopyProgress progress,
        nint data, nint cancel, uint flags);
}
