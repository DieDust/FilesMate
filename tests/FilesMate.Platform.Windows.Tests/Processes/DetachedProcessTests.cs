using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FilesMate.Platform.Windows.Processes;

namespace FilesMate.Platform.Windows.Tests.Processes;

[SupportedOSPlatform("windows")]
public sealed class DetachedProcessTests
{
    [Fact]
    public void Builds_windows_command_line_without_losing_spaces_or_quotes()
    {
        var command = DetachedProcess.BuildCommandLine(
            @"C:\Program Files\FilesMate\SearchHost\FilesMate.SearchHost.exe",
            ["--background", "--manager", @"C:\Program Files\FilesMate\FilesMate.App.exe", "quoted\"value", @"trailing\"]);

        Assert.Equal(
            "\"C:\\Program Files\\FilesMate\\SearchHost\\FilesMate.SearchHost.exe\" --background --manager " +
            "\"C:\\Program Files\\FilesMate\\FilesMate.App.exe\" \"quoted\\\"value\" trailing\\",
            command);
        Assert.Equal("--search-action \"a b\"", DetachedProcess.JoinArguments(["--search-action", "a b"]));
        Assert.Equal(string.Empty, DetachedProcess.JoinArguments([]));
    }

    [Fact]
    public void Job_membership_queries_are_consistent()
    {
        // A process cannot sit in a kill-on-close job without sitting in a job at all.
        if (DetachedProcess.IsInKillOnCloseJob()) Assert.True(DetachedProcess.IsInJob());
        Assert.Equal(DetachedProcess.IsInKillOnCloseJob(), (DetachedProcess.CurrentJobLimitFlags() & 0x2000) != 0);
    }

    [Fact]
    public void Child_leaves_the_callers_kill_on_close_job()
    {
        var job = KillOnCloseJob.Value;
        if (job == 0) return; // Nested jobs are refused here; the query tests above still run.
        Assert.True(DetachedProcess.IsInKillOnCloseJob());

        // An ordinary child must land in the job, otherwise an outer job (IDE agents use silent breakaway)
        // is already diverting children and this environment cannot demonstrate inheritance at all.
        using var ordinary = Process.Start(new ProcessStartInfo(Cmd, "/c timeout /t 20 /nobreak >nul") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Assert.True(IsProcessInJob(ordinary.Handle, job, out var inherited));
            if (!inherited) return;
        }
        finally { ordinary.Kill(entireProcessTree: true); }

        var pid = DetachedProcess.Start(Cmd, ["/c", "timeout /t 20 /nobreak >nul"], hidden: true);
        Assert.NotEqual(0, pid);
        using var child = Process.GetProcessById(pid);
        try
        {
            Assert.True(IsProcessInJob(child.Handle, job, out var inOurJob));
            Assert.False(inOurJob);
        }
        finally { child.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void Explorer_hosted_launch_is_available_on_an_interactive_desktop()
    {
        if (Process.GetProcessesByName("explorer").Length == 0) return;
        // rundll32 without arguments exits at once and shows no window; the assertion is about reaching
        // the desktop's shell object, not about what it launches.
        Assert.True(DetachedProcess.TryOpenViaExplorer(Path.Combine(Environment.SystemDirectory, "rundll32.exe")));
    }

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>
    /// Puts the test host into a job that kills its members when the last handle closes. The handle is kept
    /// for the lifetime of the process, so the only members terminated are children that did not break away.
    /// </summary>
    private static readonly Lazy<nint> KillOnCloseJob = new(() =>
    {
        var job = CreateJobObjectW(0, null);
        if (job == 0) return 0;
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = 0x2000 /* KILL_ON_JOB_CLOSE */ | 0x0800 /* BREAKAWAY_OK */;
        if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>())
            || !AssignProcessToJobObject(job, GetCurrentProcess()))
        {
            CloseHandle(job);
            return 0;
        }
        return job;
    });

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int informationClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION information, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
}
