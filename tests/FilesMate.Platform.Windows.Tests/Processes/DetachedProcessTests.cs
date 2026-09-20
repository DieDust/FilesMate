using System.Diagnostics;

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
    public void Explorer_hosted_launch_is_available_on_an_interactive_desktop()
    {
        if (Process.GetProcessesByName("explorer").Length == 0) return;
        // rundll32 without arguments exits at once and shows no window; the assertion is about reaching
        // the desktop's shell object, not about what it launches.
        Assert.True(DetachedProcess.TryOpenViaExplorer(Path.Combine(Environment.SystemDirectory, "rundll32.exe")));
    }

}
