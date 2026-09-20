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
}
