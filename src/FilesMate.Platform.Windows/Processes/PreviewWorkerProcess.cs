using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FilesMate.Platform.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Processes;

/// <summary>Resource containment for our managed converter; this is not a security sandbox.</summary>
public sealed class PreviewWorkerProcess : IDisposable
{
    private readonly SafeFileHandle _job;
    public Process Process { get; }
    private PreviewWorkerProcess(SafeFileHandle job, Process process) { _job = job; Process = process; }

    public static PreviewWorkerProcess Start(string executable, IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var job = CreateJobObjectW(0, null);
        if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        var information = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new()
            {
                LimitFlags = 0x2000 | 0x100 | 0x8, // kill on owner exit, per-process memory, one process
                ActiveProcessLimit = 1,
            },
            ProcessMemoryLimit = 384u * 1024 * 1024,
        };
        var startup = new STARTUPINFOW { cb = Marshal.SizeOf<STARTUPINFOW>() };
        PROCESS_INFORMATION child = default;
        var transferred = false;
        try
        {
            if (!SetInformationJobObject(job, 9, ref information, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!Kernel32.CreateProcess(executable, new StringBuilder(DetachedProcess.BuildCommandLine(executable, arguments)),
                    0, 0, false, Kernel32.CreateSuspended | Kernel32.CreateNoWindow, 0, Path.GetDirectoryName(executable), ref startup, out child))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!AssignProcessToJobObject(job, child.hProcess)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var process = Process.GetProcessById(child.dwProcessId);
            if (Kernel32.ResumeThread(child.hThread) == uint.MaxValue)
            { process.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            transferred = true;
            return new(job, process);
        }
        finally
        {
            if (!transferred)
            {
                if (child.hProcess != 0) Kernel32.TerminateProcess(child.hProcess, 1);
                job.Dispose();
            }
            if (child.hThread != 0) Kernel32.CloseHandle(child.hThread);
            if (child.hProcess != 0) Kernel32.CloseHandle(child.hProcess);
        }
    }

    public void Terminate() => _job.Dispose();
    public void Dispose() { Terminate(); Process.Dispose(); }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
}
