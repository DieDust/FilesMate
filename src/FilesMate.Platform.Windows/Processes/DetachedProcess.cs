using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Processes;

/// <summary>
/// Starts processes that must outlive whatever started us.
/// Terminals, IDE agents, installers and test harnesses wrap their children in a Windows Job Object with
/// KILL_ON_JOB_CLOSE. Every process we create silently joins that job, so when the tool exits the kernel
/// also terminates the resident search host and any application the user launched through it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DetachedProcess
{
    /// <summary>True when the current process belongs to a job whose closure terminates its members.</summary>
    public static bool IsInKillOnCloseJob() => (CurrentJobLimitFlags() & Kernel32.JobObjectLimitKillOnJobClose) != 0;

    /// <summary>True when the current process belongs to any job.</summary>
    public static bool IsInJob() => Kernel32.IsProcessInJob(Kernel32.GetCurrentProcess(), 0, out var inJob) && inJob;

    internal static uint CurrentJobLimitFlags()
    {
        var information = default(JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
        return Kernel32.QueryInformationJobObject(0, Kernel32.JobObjectExtendedLimitInformation, ref information,
            Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), out _)
            ? information.BasicLimitInformation.LimitFlags
            : 0;
    }

    /// <summary>
    /// Starts an executable outside the caller's job. Returns the child's process id, or 0 when Explorer
    /// performed the launch on our behalf because the job forbids breakaway.
    /// </summary>
    /// <exception cref="Win32Exception">The executable could not be started by any route.</exception>
    public static int Start(string executable, IReadOnlyList<string> arguments, bool hidden = false)
    {
        var directory = Path.GetDirectoryName(executable);
        var flags = hidden ? Kernel32.CreateNoWindow : 0u;
        if (TryCreateProcess(executable, arguments, directory, flags | Kernel32.CreateBreakawayFromJob, out var pid, out var error)) return pid;
        // A successful CREATE_BREAKAWAY_FROM_JOB can leave the child inside an outer job. The child is
        // checked while suspended below, before it can execute user code. Delegate to the desktop shell
        // if any job remains; never silently retry with inherited lifetime.
        if (error != Kernel32.ErrorAccessDenied || !IsInJob()) throw new Win32Exception(error);
        if (TryOpenViaExplorer(executable, JoinArguments(arguments), directory: directory)) return 0;
        throw new Win32Exception(error);
    }

    /// <summary>
    /// Opens a shell target (file, folder, URL, shortcut or <c>shell:AppsFolder\…</c> item) with its default
    /// verb. Inside a job the launch is delegated to Explorer so the target does not inherit our lifetime.
    /// </summary>
    public static void Open(string target, string? arguments = null, string? directory = null)
    {
        // QueryInformationJobObject only describes the immediate job, not every ancestor. Shell targets
        // cannot be suspended and checked like executables, so delegate whenever any job is present.
        if (IsInJob())
        {
            if (TryOpenViaExplorer(target, arguments, directory: directory)) return;
            throw new Win32Exception(Kernel32.ErrorAccessDenied, "Could not start the application independently through Windows Explorer.");
        }
        var start = new ProcessStartInfo(target) { UseShellExecute = true };
        if (arguments is not null) start.Arguments = arguments;
        if (directory is not null) start.WorkingDirectory = directory;
        using var process = Process.Start(start);
    }

    /// <summary>
    /// Asks the desktop's Explorer to ShellExecute the target. The new process becomes Explorer's child and
    /// therefore leaves any job the caller is confined to. Returns false when Explorer is unavailable.
    /// </summary>
    public static bool TryOpenViaExplorer(string file, string? arguments = null, string? verb = null, string? directory = null)
    {
        try
        {
            var shell = DesktopShell();
            if (shell is null) return false;
            shell.ShellExecute(file, arguments ?? string.Empty, directory ?? string.Empty, verb ?? string.Empty, (int)Shell32.SwShownormal);
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException or Win32Exception
            or UnauthorizedAccessException or NotSupportedException or ArgumentException or MissingMemberException)
        {
            return false;
        }
    }

    private static IShellDispatch2? DesktopShell()
    {
        var type = Type.GetTypeFromCLSID(Shell32.ClsidShellWindows);
        if (type is null) return null;
        var windows = (IShellWindows)Activator.CreateInstance(type)!;
        object location = Shell32.CsidlDesktop;
        object root = null!;
        var desktop = windows.FindWindowSW(ref location, ref root, Shell32.SwcDesktop, out _, Shell32.SwfoNeedDispatch);
        if (desktop is null) return null;
        var service = Shell32.SidSTopLevelBrowser;
        var iid = Shell32.IidIShellBrowser;
        if (((IShellServiceProvider)desktop).QueryService(ref service, ref iid, out var browserObject) < 0) return null;
        var browser = (IShellBrowser)browserObject;
        if (browser.QueryActiveShellView(out var view) < 0) return null;
        var dispatch = Shell32.IidIDispatch;
        if (view.GetItemObject(Shell32.SvgioBackground, ref dispatch, out var background) < 0) return null;
        return ((IShellFolderViewDual)background).Application as IShellDispatch2;
    }

    private static bool TryCreateProcess(string executable, IReadOnlyList<string> arguments, string? directory, uint flags, out int pid, out int error)
    {
        var startup = new STARTUPINFOW { cb = Marshal.SizeOf<STARTUPINFOW>() };
        var commandLine = new StringBuilder(BuildCommandLine(executable, arguments));
        if (!Kernel32.CreateProcess(executable, commandLine, 0, 0, false, flags | Kernel32.CreateSuspended, 0, directory, ref startup, out var process))
        {
            error = Marshal.GetLastWin32Error();
            pid = 0;
            return false;
        }
        var resumed = false;
        pid = 0;
        try
        {
            if (!Kernel32.IsProcessInJob(process.hProcess, 0, out var inJob))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            if (inJob)
            {
                error = Kernel32.ErrorAccessDenied;
                return false;
            }
            if (Kernel32.ResumeThread(process.hThread) == uint.MaxValue)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            resumed = true;
            error = 0;
            pid = process.dwProcessId;
            return true;
        }
        finally
        {
            // This is only our newly created, still-suspended child. It has executed no application code.
            if (!resumed) Kernel32.TerminateProcess(process.hProcess, 1);
            Kernel32.CloseHandle(process.hThread);
            Kernel32.CloseHandle(process.hProcess);
        }
    }

    /// <summary>Builds a CreateProcess command line that CommandLineToArgvW parses back into the same arguments.</summary>
    internal static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var command = new StringBuilder(Quote(executable));
        foreach (var argument in arguments) command.Append(' ').Append(Quote(argument));
        return command.ToString();
    }

    internal static string JoinArguments(IReadOnlyList<string> arguments)
    {
        return WindowsCommandLine.JoinArguments(arguments);
    }

    private static string Quote(string value)
    {
        return WindowsCommandLine.Quote(value);
    }
}
