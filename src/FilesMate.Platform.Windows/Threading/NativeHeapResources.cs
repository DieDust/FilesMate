using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Threading;

public static class NativeHeapResources
{
    // Decommit unused heap blocks after native controls have been destroyed.
    // This does not evict live process pages from the working set.
    public static bool ReleaseUnused()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var information = new OptimizeInformation { Version = 1 };
        return HeapSetInformation(0, 3, ref information, 8);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OptimizeInformation
    {
        public uint Version;
        public uint Flags;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HeapSetInformation(nint heap, int informationClass,
        ref OptimizeInformation information, nuint length);
}
