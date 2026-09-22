using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Archives;

// Ancestor directory handles deny delete/rename for the duration of each operation.
// OPEN_REPARSE_POINT examines the object itself rather than following a last-component link.
internal static class ArchivePathGuard
{
    internal static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || path[2] is not ('\\' or '/') || path.Length > 4096)
            throw Error(ArchiveErrorCode.InvalidPath, path);
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        if (normalized.Length == 2) normalized += '\\';
        foreach (var part in normalized[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries)) ValidateSegment(part);
        var full = Path.GetFullPath(normalized);
        if (new DriveInfo(full[..3]).DriveType == DriveType.Network)
            throw Error(ArchiveErrorCode.InvalidPath, path);
        return full;
    }

    internal static void ValidateSegment(string segment)
    {
        if (segment.Length == 0 || segment.Length > 255 || segment is "." or ".." ||
            segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
            throw Error(ArchiveErrorCode.InvalidPath, segment);
        var stem = segment.Split('.')[0].TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (stem[3] is >= '0' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3')))
            throw Error(ArchiveErrorCode.InvalidPath, segment);
    }

    internal static DirectoryLease LockDirectory(string path, bool createLeaf = false)
    {
        var lease = new DirectoryLease();
        try
        {
            var parts = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var current = path[..3];
            lease.Add(OpenDirectory(current));
            for (var i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                if (createLeaf && i == parts.Length - 1 && !Directory.Exists(current))
                    Directory.CreateDirectory(current);
                lease.Add(OpenDirectory(current));
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal static SafeFileHandle OpenDirectory(string path)
    {
        // Metadata-only access does not participate in Windows share access checks.
        // Request actual directory read access so another process cannot rename it
        // or obtain GENERIC_WRITE to replace it with a reparse point.
        var handle = CreateFileW(path, 0x80000000, FileShare.Read, 0, 3, 0x02200000, 0);
        try
        {
            var info = Inspect(handle);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw Error(ArchiveErrorCode.UnsafeLink, path);
            if ((info.Attributes & FileAttributes.Directory) == 0) throw Error(ArchiveErrorCode.InvalidPath, path);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static FileStream OpenRead(string path, out FileIdentity identity)
    {
        var handle = CreateFileW(path, 0x80000000, FileShare.Read, 0, 3, 0x48200000, 0);
        try
        {
            var info = Inspect(handle);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Links != 1)
                throw Error(ArchiveErrorCode.UnsafeLink, path);
            if ((info.Attributes & FileAttributes.Directory) != 0) throw Error(ArchiveErrorCode.InvalidPath, path);
            identity = new(info.Volume, ((ulong)info.IdHigh << 32) | info.IdLow,
                ((long)info.SizeHigh << 32) | info.SizeLow, ((long)info.LastWrite.High << 32) | info.LastWrite.Low);
            return new FileStream(handle, FileAccess.Read, ZipArchiveService.BufferSize, isAsync: true);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static FileStream CreateNew(string path)
    {
        var handle = CreateFileW(path, 0xC0010000, FileShare.Read, 0, 1, 0x48200000, 0);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (code is 80 or 183) throw Error(ArchiveErrorCode.DestinationExists, path);
            throw Failure(code);
        }
        return new FileStream(handle, FileAccess.ReadWrite, ZipArchiveService.BufferSize, isAsync: true);
    }

    internal static void DeleteOwned(FileStream stream)
    {
        var disposition = 1u;
        if (!SetFileInformationByHandle(stream.SafeFileHandle, 21, ref disposition, sizeof(uint)))
            throw Failure(Marshal.GetLastWin32Error());
    }

    internal static void SetLastWriteTime(FileStream stream, DateTimeOffset value)
    {
        var ticks = value.ToFileTime();
        var time = new FileTime { Low = (uint)ticks, High = (uint)(ticks >> 32) };
        if (!SetFileTime(stream.SafeFileHandle, 0, 0, ref time)) throw Failure(Marshal.GetLastWin32Error());
    }

    private static FileInformation Inspect(SafeFileHandle handle)
    {
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            throw Failure(Marshal.GetLastWin32Error());
        return info;
    }

    private static void ValidateDirectoryPath(string path, SafeFileHandle held)
    {
        var expected = Inspect(held);
        using var current = CreateFileW(path, 0x80, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x02200000, 0);
        var actual = Inspect(current);
        if ((expected.Attributes & FileAttributes.ReparsePoint) != 0 || (actual.Attributes & FileAttributes.ReparsePoint) != 0 ||
            actual.Volume != expected.Volume || actual.IdHigh != expected.IdHigh || actual.IdLow != expected.IdLow)
            throw Error(ArchiveErrorCode.UnsafeLink, path);
    }

    internal static ArchiveOperationException Error(ArchiveErrorCode code, string? detail = null) => new(code, detail is null ? code.ToString() : $"{code}: {detail}");
    private static IOException Failure(int code) => new(new Win32Exception(code).Message, unchecked((int)(0x80070000u | (uint)code)));

    internal readonly record struct FileIdentity(uint Volume, ulong Id, long Length, long LastWrite);
    internal sealed class DirectoryLease : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];
        private readonly List<FileStream> _markers = [];
        internal void Add(SafeFileHandle handle) => _handles.Add(handle);
        internal void KeepOwnedDirectoryNonempty(string path)
        {
            // Share modes do not block FILE_WRITE_ATTRIBUTES. A retained child prevents
            // FSCTL_SET_REPARSE_POINT converting an otherwise empty staging directory.
            // Never call this on user source directories. Only staging is writable here.
            var directory = _handles[^1];
            ValidateDirectoryPath(path, directory);
            var marker = CreateNew(Path.Combine(path, ".filesmate-zip-owner-" + Guid.NewGuid().ToString("N")));
            try
            {
                ValidateDirectoryPath(path, directory);
                _markers.Add(marker);
            }
            catch
            {
                try { DeleteOwned(marker); }
                finally { marker.Dispose(); }
                throw;
            }
        }

        public void Dispose()
        {
            Exception? failure = null;
            for (var i = _markers.Count - 1; i >= 0; i--)
            {
                try { DeleteOwned(_markers[i]); }
                catch (IOException error) { failure ??= error; }
                finally { _markers[i].Dispose(); }
            }
            for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public FileAttributes Attributes;
        public FileTime Created, Accessed, LastWrite;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, nint security, uint mode, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref uint flags, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileTime(SafeFileHandle handle, nint creation, nint access, ref FileTime write);
}
