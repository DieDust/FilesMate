using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Search;

/// <summary>
/// The resident search host and everything it launches must not inherit a kill-on-close Windows job from
/// whichever installer, terminal or IDE agent happened to start it; otherwise closing that tool also closes
/// the host and the foreground applications the user opened through the palette.
/// </summary>
public sealed class SearchHostProcessIsolationContractTests
{
    private static string HostRoot => Path.Combine(ThemeXaml.RepoRoot, "src", "FilesMate.SearchHost");

    [Fact]
    public void Host_leaves_a_kill_on_close_job_before_becoming_resident()
    {
        var program = File.ReadAllText(Path.Combine(HostRoot, "Program.cs"));
        var detach = program.IndexOf("DetachedProcess.IsInKillOnCloseJob()", StringComparison.Ordinal);
        Assert.True(detach > 0);
        Assert.Contains("\"--detached\"", program, StringComparison.Ordinal);
        Assert.Contains("DetachedProcess.Start(Environment.ProcessPath!", program, StringComparison.Ordinal);
        // Relaunch happens before the single-instance mutex, so the detached copy can take it over.
        Assert.True(detach < program.IndexOf("new Mutex(true", StringComparison.Ordinal));
        // One-shot maintenance commands run inline; only the resident host pays for a relaunch.
        Assert.True(detach > program.IndexOf("\"--sync-startup\"", StringComparison.Ordinal));
        Assert.True(detach > program.IndexOf("\"--stop\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_launch_from_the_host_goes_through_the_detached_launcher()
    {
        foreach (var file in Directory.GetFiles(HostRoot, "*.cs"))
        {
            var code = File.ReadAllText(file);
            Assert.DoesNotContain("UseShellExecute = true", code, StringComparison.Ordinal);
        }
        var program = File.ReadAllText(Path.Combine(HostRoot, "Program.cs"));
        Assert.Contains("DetachedProcess.Start(ManagerPath, arguments)", program, StringComparison.Ordinal);
        Assert.Contains("DetachedProcess.Start(ManagerPath, [\"--search-action\", id])", program, StringComparison.Ordinal);
        var shell = File.ReadAllText(Path.Combine(HostRoot, "ApplicationShell.cs"));
        Assert.Contains("DetachedProcess.Open(entry.LaunchPath)", shell, StringComparison.Ordinal);
        Assert.Contains("DetachedProcess.IsInJob()", shell, StringComparison.Ordinal);
        Assert.Contains("DetachedProcess.Open(row.Path)", File.ReadAllText(Path.Combine(HostRoot, "PaletteWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("DetachedProcess.Open(link.AbsoluteUri)", File.ReadAllText(Path.Combine(HostRoot, "PaletteWindow.Documents.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void File_manager_starts_the_host_and_the_installer_outside_its_own_job()
    {
        Assert.Contains("DetachedProcess.Start(HostPath, arguments, hidden: true)",
            File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Services", "GlobalSearchService.cs")), StringComparison.Ordinal);
        Assert.Contains("DetachedProcess.Start(installer, start.ArgumentList)",
            File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Updates", "UpdateController.cs")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(ThemeXaml.AppRoot, "Services", "DetachedProcessLauncher.cs")));
    }
}
