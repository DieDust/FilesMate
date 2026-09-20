using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Operations;

internal static class FileStreamSize
{
    internal static long Read(string path)
    {
        var handle = FindFirstStreamW(path, 0, out var data, 0);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 87) return new FileInfo(path).Length; // Filesystem has no named streams.
            throw new IOException("Could not inspect file streams.", new Win32Exception(error));
        }
        try
        {
            long size = 0;
            do { size = checked(size + data.Size); }
            while (FindNextStreamW(handle, out data));
            var error = Marshal.GetLastWin32Error();
            if (error != 38) throw new IOException("Could not inspect all file streams.", new Win32Exception(error));
            return size;
        }
        finally { FindClose(handle); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StreamData
    {
        public long Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(string path, int level, out StreamData data, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(IntPtr handle, out StreamData data);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);
}
