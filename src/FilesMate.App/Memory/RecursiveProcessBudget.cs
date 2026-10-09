using System.Diagnostics;
using System.Runtime.InteropServices;
using FilesMate.Core.Recursive;

namespace FilesMate.App.Memory;

internal static class RecursiveProcessBudget
{
    // Combined admission for all tabs/windows/panes, never a per-pane RAM claim.
    internal static readonly RecursiveMemoryBudgetPool Shared = new(512L * 1024 * 1024, ReadHeadroom);

    private static RecursiveMemoryHeadroom ReadHeadroom()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) return default;
        using var process = Process.GetCurrentProcess();
        var privateBytes = process.PrivateMemorySize64;
        var runtime = Math.Max(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes - privateBytes - 128L * 1024 * 1024);
        var address = (long)Math.Min(status.AvailableVirtual, (ulong)long.MaxValue) - 128L * 1024 * 1024;
        return new((long)Math.Min(status.AvailablePhysical, (ulong)long.MaxValue), runtime, Math.Max(0, address));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
