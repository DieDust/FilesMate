using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FilesMate.Platform.Windows.Tests.Processes;

[SupportedOSPlatform("windows")]
public sealed class NestedJobLifetimeTests
{
    [Theory]
    [InlineData(0x2000u)] // A job that forbids breakaway.
    [InlineData(0x2800u)] // Breakaway succeeds from the inner job, but the outer job remains.
    [InlineData(0x0800u)] // The immediate job has no kill flag; its ancestor still does.
    public void Independent_children_survive_outer_job_closure(uint innerFlags)
    {
        // Explorer delegation requires an interactive desktop. CI without a desktop cannot exercise it.
        var explorers = Process.GetProcessesByName("explorer");
        try { if (explorers.Length == 0) return; }
        finally { foreach (var explorer in explorers) explorer.Dispose(); }
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-job-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var outer = CreateJob(0x2000);
        var inner = CreateJob(innerFlags);
        PROCESS_INFORMATION broker = default;
        var workers = new List<Process>();
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "ProcessIsolationFixture", "FilesMate.ProcessIsolation.Fixture.exe");
            var startup = new STARTUPINFO { Size = Marshal.SizeOf<STARTUPINFO>() };
            Assert.True(CreateProcessW(executable, new StringBuilder($"\"{executable}\" broker \"{directory}\""),
                0, 0, false, 0x08000004, 0, null, ref startup, out broker));
            Assert.True(AssignProcessToJobObject(outer, broker.Process));
            Assert.True(AssignProcessToJobObject(inner, broker.Process));
            Assert.NotEqual(uint.MaxValue, ResumeThread(broker.Thread));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(directory, "ready")) || File.Exists(Path.Combine(directory, "error")), 20000));
            Assert.False(File.Exists(Path.Combine(directory, "error")), ReadIfPresent(Path.Combine(directory, "error")));
            foreach (var name in new[] { "ordinary", "independent", "shell" })
            {
                var path = Path.Combine(directory, name + ".pid");
                var id = 0;
                Assert.True(SpinWait.SpinUntil(() => int.TryParse(ReadIfPresent(path), out id), 10000));
                workers.Add(Process.GetProcessById(id));
            }
            Assert.True(IsProcessInJob(workers[0].Handle, outer, out var ordinaryInJob));
            Assert.True(ordinaryInJob);
            foreach (var worker in workers.Skip(1))
            {
                Assert.True(IsProcessInJob(worker.Handle, outer, out var inherited));
                Assert.False(inherited);
            }
            CloseHandle(inner);
            inner = 0;
            CloseHandle(outer);
            outer = 0;
            Assert.True(workers[0].WaitForExit(5000));
            foreach (var worker in workers.Skip(1)) Assert.False(worker.WaitForExit(300));
        }
        finally
        {
            if (inner != 0) CloseHandle(inner);
            if (outer != 0) CloseHandle(outer);
            if (broker.Process != 0) { TerminateProcess(broker.Process, 0); CloseHandle(broker.Process); }
            if (broker.Thread != 0) CloseHandle(broker.Thread);
            foreach (var worker in workers)
            {
                try { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(5000); } }
                finally { worker.Dispose(); }
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReadIfPresent(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; }
        catch (IOException) { return string.Empty; }
    }

    private static nint CreateJob(uint flags)
    {
        var job = CreateJobObjectW(0, null);
        Assert.NotEqual(0, job);
        var limits = new JOB_LIMITS { Basic = new BASIC_LIMITS { Flags = flags } };
        if (SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JOB_LIMITS>())) return job;
        CloseHandle(job);
        throw new System.ComponentModel.Win32Exception();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint job, int cls, ref JOB_LIMITS information, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string executable, StringBuilder command, nint processAttributes,
        nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref STARTUPINFO startup, out PROCESS_INFORMATION process);
    [DllImport("kernel32.dll")] private static extern bool IsProcessInJob(nint process, nint job, out bool inside);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(nint process, uint code);

    [StructLayout(LayoutKind.Sequential)]
    private struct BASIC_LIMITS
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinWorkingSet, MaxWorkingSet;
        public uint MaxProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JOB_LIMITS
    {
        public BASIC_LIMITS Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int Size;
        public nint Reserved, Desktop, Title;
        public int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public nint ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint Process, Thread;
        public int ProcessId, ThreadId;
    }
}
