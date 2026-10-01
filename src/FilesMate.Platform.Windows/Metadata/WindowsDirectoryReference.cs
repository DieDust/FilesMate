using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Metadata;

/// <summary>A directory identity that survives renames on the same volume.</summary>
public sealed record WindowsDirectoryReference(string VolumePath, ulong VolumeSerial, ulong FileId, ulong Created);

public static class WindowsDirectoryReferences
{
    private const uint DirectoryFlags = 0x02000000 | 0x00200000;
    private const FileShare Sharing = FileShare.ReadWrite | FileShare.Delete;

    public static WindowsDirectoryReference? Capture(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var handle = CreateFileW(path, 0x80, Sharing, 0, 3, DirectoryFlags, 0);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x10) == 0) return null;
        var final = FinalPath(handle, 1);
        var end = final?.IndexOf('}') ?? -1;
        if (end < 0 || final is null || !final.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)) return null;
        var id = ((ulong)info.IndexHigh << 32) | info.IndexLow;
        return info.VolumeSerial == 0 || id == 0 ? null : new(final[..(end + 2)], info.VolumeSerial, id, Created(info));
    }

    public static string? Resolve(WindowsDirectoryReference reference)
    {
        if (!OperatingSystem.IsWindows() || reference.FileId == 0 || reference.VolumeSerial == 0 || string.IsNullOrEmpty(reference.VolumePath)
            || !reference.VolumePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)) return null;
        using var volume = CreateFileW(reference.VolumePath, 0, Sharing, 0, 3, DirectoryFlags, 0);
        if (volume.IsInvalid || !GetFileInformationByHandle(volume, out var volumeInfo) || volumeInfo.VolumeSerial != reference.VolumeSerial) return null;
        var descriptor = new FileIdDescriptor { Size = 24, Id = reference.FileId };
        using var directory = OpenFileById(volume, ref descriptor, 0x80, Sharing, 0, DirectoryFlags);
        if (directory.IsInvalid || !GetFileInformationByHandle(directory, out var info)
            || (info.Attributes & 0x10) == 0 || info.VolumeSerial != reference.VolumeSerial
            || (((ulong)info.IndexHigh << 32) | info.IndexLow) != reference.FileId || Created(info) != reference.Created) return null;
        var path = FinalPath(directory, 0);
        if (path is null) return null;
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        return Directory.Exists(path) ? path : null;
    }

    private static ulong Created(Information info) => ((ulong)info.CreatedHigh << 32) | info.CreatedLow;

    private static string? FinalPath(SafeFileHandle handle, uint flags)
    {
        var buffer = new char[512];
        var count = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, flags);
        if (count >= buffer.Length && count < 32768)
        {
            buffer = new char[count + 1];
            count = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, flags);
        }
        return count > 0 && count < buffer.Length ? new string(buffer, 0, (int)count) : null;
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private struct FileIdDescriptor { public uint Size; public uint Type; public ulong Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(SafeFileHandle volume, ref FileIdDescriptor id, uint access, FileShare share, nint security, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] buffer, uint size, uint flags);
}
