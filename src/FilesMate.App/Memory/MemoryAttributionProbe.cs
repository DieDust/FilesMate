#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FilesMate.App.Memory;

internal static class MemoryAttributionProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct HeapTotals
    {
        public uint Size;
        public nuint Allocated, Committed, Reserved, Maximum;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Region
    {
        public nuint Address, Allocation;
        public uint AllocationProtect;
        public ushort Partition;
        public nuint Size;
        public uint State, Protect, Type;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WorkingPage { public nuint Address, Flags; }
    internal sealed class RegionTotals
    {
        public ulong Committed { get; set; }
        public ulong Resident { get; set; }
        public ulong PrivateResident { get; set; }
        public int Regions { get; set; }
    }
    internal static object Capture()
    {
        using var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        var heaps = new nint[GetProcessHeaps(0, null) + 16];
        var heapCount = GetProcessHeaps((uint)heaps.Length, heaps);
        var native = new List<object>();
        for (var i = 0; i < Math.Min(heapCount, heaps.Length); i++)
        {
            var totals = new HeapTotals { Size = (uint)Marshal.SizeOf<HeapTotals>() };
            var success = HeapSummary(heaps[i], 0, ref totals);
            native.Add(new { Address = heaps[i].ToString("x"), Success = success,
                Error = success ? 0 : Marshal.GetLastWin32Error(),
                Allocated = (ulong)totals.Allocated, Committed = (ulong)totals.Committed });
        }
        var categories = new Dictionary<string, RegionTotals>();
        var allocations = new Dictionary<string, RegionTotals>();
        var pages = new WorkingPage[1024];
        nuint address = 0;
        while (VirtualQueryEx(process.Handle, address, out var region, (nuint)Marshal.SizeOf<Region>()) != 0)
        {
            if (region.State == 0x1000)
            {
                var key = region.Type switch { 0x1000000 => "Image", 0x40000 => "Mapped", _ =>
                    (region.Protect & 0xf0) != 0 ? "PrivateExecutable" : "PrivateData" };
                if (!categories.TryGetValue(key, out var total)) categories[key] = total = new();
                total.Committed += region.Size;
                total.Regions++;
                RegionTotals? allocation = null;
                if (region.Type == 0x20000)
                {
                    var allocationKey = region.Allocation.ToString("x");
                    if (!allocations.TryGetValue(allocationKey, out allocation)) allocations[allocationKey] = allocation = new();
                    allocation.Committed += region.Size;
                    allocation.Regions++;
                }
                for (nuint offset = 0; offset < region.Size;)
                {
                    var count = (int)Math.Min((nuint)pages.Length, (region.Size - offset) / 4096);
                    if (count == 0) break;
                    for (var i = 0; i < count; i++) pages[i] = new() { Address = region.Address + offset + (nuint)(i * 4096) };
                    if (QueryWorkingSetEx(process.Handle, pages, (uint)(count * Marshal.SizeOf<WorkingPage>())))
                        for (var i = 0; i < count; i++)
                            if ((pages[i].Flags & 1) != 0)
                            {
                                total.Resident += 4096;
                                if (allocation is not null) allocation.Resident += 4096;
                                if ((pages[i].Flags & 0x8000) == 0)
                                {
                                    total.PrivateResident += 4096;
                                    if (allocation is not null) allocation.PrivateResident += 4096;
                                }
                            }
                    offset += (nuint)(count * 4096);
                }
            }
            var next = region.Address + region.Size;
            if (next <= address) break;
            address = next;
        }
        return new { Time = DateTimeOffset.Now, process.Id, process.PrivateMemorySize64,
            process.WorkingSet64, ManagedUsed = GC.GetTotalMemory(false),
            ManagedCommitted = gc.TotalCommittedBytes, gc.HeapSizeBytes, gc.FragmentedBytes,
            gc.FinalizationPendingCount, Gen2 = GC.CollectionCount(2),
            ThreadCount = process.Threads.Count, GcPauseMilliseconds = GC.GetTotalPauseDuration().TotalMilliseconds,
            HeapSummaries = native, Regions = categories,
            PrivateAllocations = allocations,
            Modules = process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).Order().ToArray(),
            ImageCaches = Icons.ShellIconBinder.CacheStatistics };
    }
    [DllImport("kernel32.dll")] private static extern uint GetProcessHeaps(uint count, [Out] nint[]? heaps);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HeapSummary(nint heap, uint flags, ref HeapTotals totals);
    [DllImport("kernel32.dll")] private static extern nuint VirtualQueryEx(nint process, nuint address, out Region region, nuint size);
    [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryWorkingSetEx(nint process, [In, Out] WorkingPage[] pages, uint bytes);
}
#endif
