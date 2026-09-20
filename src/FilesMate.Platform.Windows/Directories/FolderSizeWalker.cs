using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Directories;

/// <summary>
/// Recursive logical size of a folder. Skips reparse points so junctions do not loop.
/// Access-denied subtrees are omitted rather than failing the whole walk.
/// </summary>
public static class FolderSizeWalker
{
    private const uint OnDiskFlags = Kernel32.FindFirstExLargeFetch | Kernel32.FindFirstExOnDiskEntriesOnly;

    public static ulong Measure(
        string path,
        CancellationToken cancellationToken,
        Action<ulong>? progress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        cancellationToken.ThrowIfCancellationRequested();

        ulong total = 0;
        var pending = new Stack<string>();
        var clock = Stopwatch.StartNew();
        pending.Push(path);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            var searchPath = directory.EndsWith('\\') ? directory + "*" : directory + "\\*";
            SafeFindHandle? handle = null;
            try
            {
                handle = OpenFind(searchPath, out var data);
                if (handle.IsInvalid)
                {
                    continue;
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = data.GetFileName();
                    if (name is not "." and not ".." and not "")
                    {
                        var attributes = (FileAttributes)data.dwFileAttributes;
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                pending.Push(directory.EndsWith('\\') ? directory + name : directory + "\\" + name);
                            }
                            else
                            {
                                total += ((ulong)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                                Report(progress, total, clock);
                            }
                        }
                    }

                    if (Kernel32.FindNextFileW(handle, out data))
                    {
                        continue;
                    }

                    var error = Marshal.GetLastPInvokeError();
                    if (error is not Kernel32.ErrorNoMoreFiles and not Kernel32.ErrorFileNotFound)
                    {
                        break;
                    }

                    break;
                }
            }
            finally
            {
                handle?.Dispose();
            }

            Report(progress, total, clock);
        }

        return total;
    }

    private static void Report(Action<ulong>? progress, ulong total, Stopwatch clock)
    {
        if (progress is null || clock.ElapsedMilliseconds < 250)
        {
            return;
        }

        clock.Restart();
        progress(total);
    }

    private static SafeFindHandle OpenFind(string searchPath, out WIN32_FIND_DATAW data)
    {
        var handle = Kernel32.FindFirstFileExW(
            searchPath,
            FINDEX_INFO_LEVELS.FindExInfoBasic,
            out data,
            FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            nint.Zero,
            OnDiskFlags);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != Kernel32.ErrorInvalidParameter)
        {
            return handle;
        }

        handle.Dispose();
        handle = Kernel32.FindFirstFileExW(
            searchPath,
            FINDEX_INFO_LEVELS.FindExInfoBasic,
            out data,
            FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            nint.Zero,
            Kernel32.FindFirstExLargeFetch);
        if (!handle.IsInvalid || Marshal.GetLastPInvokeError() != Kernel32.ErrorInvalidParameter)
        {
            return handle;
        }

        handle.Dispose();
        return Kernel32.FindFirstFileExW(
            searchPath,
            FINDEX_INFO_LEVELS.FindExInfoBasic,
            out data,
            FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            nint.Zero,
            0);
    }
}
