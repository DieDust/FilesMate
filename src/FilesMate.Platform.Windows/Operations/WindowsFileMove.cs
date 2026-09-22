using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Operations;

internal static class WindowsFileMove
{
    internal static bool TryRename(string source, string destination)
    {
        // No MOVEFILE_COPY_ALLOWED: mount points and volumes must use our cancelable copy path.
        if (MoveFileExW(source, destination, 0)) return true;
        var error = Marshal.GetLastWin32Error();
        if (error == 17) return false; // ERROR_NOT_SAME_DEVICE
        // A read-only source can report ACCESS_DENIED before the volume check. Confirm
        // the actual volume (including directory mount points) before taking the copy path.
        if (error == 5 && AreDifferentVolumes(source, Path.GetDirectoryName(destination)!)) return false;
        throw Failure(error);
    }

    private static bool AreDifferentVolumes(string source, string destinationDirectory)
    {
        using var sourceHandle = CreateFileW(source, 0, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x00200000, 0);
        using var parentHandle = CreateFileW(destinationDirectory, 0, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x02000000, 0);
        return !sourceHandle.IsInvalid && !parentHandle.IsInvalid &&
            GetFileInformationByHandle(sourceHandle, out var sourceInfo) && GetFileInformationByHandle(parentHandle, out var parentInfo) &&
            sourceInfo.Volume != 0 && parentInfo.Volume != 0 && sourceInfo.Volume != parentInfo.Volume;
    }

    internal sealed class SourceLease : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly FileInformation _identity;
        private readonly bool _canDelete;

        internal SourceLease(string path, bool denyWrites = true, bool metadataOnly = false)
        {
            _canDelete = !metadataOnly;
            _handle = CreateFileW(path, metadataOnly ? 0 : 0x80000000u | 0x00010000u,
                denyWrites ? FileShare.Read | FileShare.Delete : FileShare.ReadWrite | FileShare.Delete,
                0, 3, 0x00200000, 0); // OPEN_EXISTING, OPEN_REPARSE_POINT
            try
            {
                if (_handle.IsInvalid || !GetFileInformationByHandle(_handle, out _identity)) throw Failure(Marshal.GetLastWin32Error());
                if ((_identity.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                    _identity.Volume == 0 || (_identity.IdHigh == 0 && _identity.IdLow == 0))
                    throw new IOException("The source file cannot be identified safely.");
            }
            catch { _handle.Dispose(); throw; }
        }

        internal void ValidatePath(string path)
        {
            using var current = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x00200000, 0);
            ValidateHandle(current);
        }

        internal void ValidateCopyHandle(nint handle)
        {
            // CopyFileEx owns this handle. Binding to it closes a path-swap/restore race
            // that checking only the source name before and after copying cannot detect.
            using var current = new SafeFileHandle(handle, ownsHandle: false);
            ValidateHandle(current);
        }

        internal IDisposable LockPublishedPath(string path)
        {
            var guard = CreateFileW(path, 0x80000000u, FileShare.Read | FileShare.Delete, 0, 3, 0x00200000, 0);
            try
            {
                // ReplaceFile merges destination creation time/attributes. The copied object,
                // data length and last write time must still match before releasing the source.
                ValidateHandle(guard, allowReplacementMetadata: true);
                return guard;
            }
            catch { guard.Dispose(); throw; }
        }

        private void ValidateHandle(SafeFileHandle current, bool allowReplacementMetadata = false)
        {
            if (current.IsInvalid || !GetFileInformationByHandle(current, out var actual) ||
                actual.Volume != _identity.Volume || actual.IdHigh != _identity.IdHigh || actual.IdLow != _identity.IdLow ||
                actual.SizeHigh != _identity.SizeHigh || actual.SizeLow != _identity.SizeLow ||
                actual.LastWrite.High != _identity.LastWrite.High || actual.LastWrite.Low != _identity.LastWrite.Low ||
                (actual.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                (!allowReplacementMetadata && (actual.Created.High != _identity.Created.High || actual.Created.Low != _identity.Created.Low ||
                    actual.Attributes != _identity.Attributes)))
                throw new IOException("The source file changed before the move completed.");
        }

        internal void Delete()
        {
            if (!_canDelete) throw new InvalidOperationException("A metadata lease cannot remove a file.");
            // A move preserves read-only attributes at its destination, just like a rename.
            // This does not change attributes or bypass ACLs: the lease already requires DELETE.
            var flags = 1u | 0x10u; // DELETE | IGNORE_READONLY_ATTRIBUTE
            if (!SetFileInformationByHandle(_handle, 21, ref flags, sizeof(uint))) throw Failure(Marshal.GetLastWin32Error());
        }

        public void Dispose() => _handle.Dispose();
    }

    private static IOException Failure(int error) => new(new Win32Exception(error).Message,
        unchecked((int)(0x80070000u | (uint)error)));

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public FileAttributes Attributes;
        public FileTime Created;
        public FileTime Accessed;
        public FileTime LastWrite;
        public uint Volume;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Links;
        public uint IdHigh;
        public uint IdLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string source, string destination, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, nint security, uint mode, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref uint flags, uint size);
}
