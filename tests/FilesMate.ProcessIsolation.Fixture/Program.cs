using System.Diagnostics;
using System.Runtime.Versioning;
using FilesMate.Platform.Windows.Processes;

namespace FilesMate.ProcessIsolation.Fixture;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["allocation-probe", var allocationOutput])
        {
            var allocation = VirtualAlloc(0, 512u * 1024 * 1024, 0x1000 | 0x2000, 4);
            File.WriteAllText(allocationOutput, allocation == 0 ? "limited" : "unlimited");
            if (allocation != 0) VirtualFree(allocation, 0, 0x8000);
            Thread.Sleep(TimeSpan.FromMinutes(1));
            return;
        }
        if (args is ["worker", var output])
        {
            File.WriteAllText(output, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Thread.Sleep(TimeSpan.FromMinutes(1));
            return;
        }
        if (args is not ["broker", var directory]) return;
        var executable = Environment.ProcessPath!;
        using var ordinary = Process.Start(new ProcessStartInfo(executable)
        {
            ArgumentList = { "worker", Path.Combine(directory, "ordinary.pid") },
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        try
        {
            DetachedProcess.Start(executable, ["worker", Path.Combine(directory, "independent.pid")], hidden: true);
            DetachedProcess.Open(executable, "worker \"" + Path.Combine(directory, "shell.pid") + "\"");
            File.WriteAllText(Path.Combine(directory, "ready"), "ok");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(directory, "error"), error.ToString());
        }
        Thread.Sleep(TimeSpan.FromMinutes(1));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern nint VirtualAlloc(nint address, nuint size, uint type, uint protection);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool VirtualFree(nint address, nuint size, uint type);
}
