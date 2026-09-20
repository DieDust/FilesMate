using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FilesMate.Platform.Windows.Threading;

/// <summary>
/// Puts the calling thread into Windows "background mode" for the lifetime of the scope: the scheduler lowers its
/// CPU, I/O and memory priority so a long crawl (indexing, folder sizing) yields to whatever the user is doing.
/// </summary>
/// <remarks>
/// Background mode is per thread and must be ended on the same thread, so use it only on a dedicated thread or
/// dispose the scope before a pool thread is returned. On other platforms, or when the call fails, this is a no-op.
/// </remarks>
public readonly struct BackgroundThreadMode : IDisposable
{
    private const int ThreadModeBackgroundBegin = 0x00010000;
    private const int ThreadModeBackgroundEnd = 0x00020000;

    private readonly bool _entered;

    private BackgroundThreadMode(bool entered) => _entered = entered;

    public static BackgroundThreadMode Enter()
    {
        if (!OperatingSystem.IsWindows()) return new(false);
        return new(SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundBegin));
    }

    public void Dispose()
    {
        if (_entered && OperatingSystem.IsWindows()) SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundEnd);
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetCurrentThread();

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadPriority(nint thread, int priority);
}
