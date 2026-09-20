using System.Runtime.InteropServices;

using FilesMate.Core.Metadata;
using FilesMate.Platform.Windows.Paths;

using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Metadata;

public sealed class WindowsFileIdentityProvider
{
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private readonly WindowsPathNormalizer _paths;

    public WindowsFileIdentityProvider()
        : this(new WindowsPathNormalizer())
    {
    }

    public WindowsFileIdentityProvider(WindowsPathNormalizer paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public FileIdentity Resolve(string path)
    {
        var normalized = _paths.Normalize(path);
        if (TryGetStableIdentity(normalized, out var volumeSerial, out var fileId))
        {
            return FileIdentity.FromStable(volumeSerial, fileId, normalized);
        }

        return FileIdentity.FromNormalizedPath(normalized);
    }

    private static bool TryGetStableIdentity(string path, out ulong volumeSerial, out ulong fileId)
    {
        volumeSerial = 0;
        fileId = 0;
        using var handle = CreateFileW(
            path,
            GenericRead,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
        {
            return false;
        }

        volumeSerial = info.VolumeSerialNumber;
        fileId = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        return volumeSerial != 0 && fileId != 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
