using FilesMate.App.Tests.DesignSystem;
using FilesMate.App.Updates;

namespace FilesMate.App.Tests.Settings;

public sealed class UpdateInstallPolicyTests
{
    [Fact]
    public void Online_update_never_requests_other_applications_to_close_or_restart()
    {
        var arguments = UpdateInstallPolicy.Arguments(Path.Combine(Path.GetTempPath(), "FilesMate installation 资料"));
        Assert.Contains("/NOCLOSEAPPLICATIONS", arguments);
        Assert.Contains("/NOFORCECLOSEAPPLICATIONS", arguments);
        Assert.Contains("/NORESTARTAPPLICATIONS", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Equals("/CLOSEAPPLICATIONS", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("/FORCECLOSEAPPLICATIONS", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("/RESTARTAPPLICATIONS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Installer_destination_remains_one_argument_with_spaces_and_unicode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate installation 资料");
        var arguments = UpdateInstallPolicy.Arguments(directory + Path.DirectorySeparatorChar);
        Assert.Equal("/DIR=" + directory, Assert.Single(arguments, argument => argument.StartsWith("/DIR=", StringComparison.Ordinal)));
        Assert.Contains("/FILESMATEUPDATE=1", arguments);
        Assert.Contains("/NORESTART", arguments);
    }

    [Fact]
    public void Interactive_installations_also_leave_unrelated_lock_owners_running()
    {
        var repository = Path.GetFullPath(Path.Combine(ThemeXaml.AppRoot, "..", ".."));
        var lines = File.ReadAllLines(Path.Combine(repository, "installer", "FilesMate.iss"));
        var section = "";
        var directives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { section = line; continue; }
            if (section != "[Setup]" || line.StartsWith(';')) continue;
            var separator = line.IndexOf('=');
            if (separator > 0) directives.Add(line[..separator].Trim(), line[(separator + 1)..].Trim());
        }
        // Inno Setup filters the files it checks for locks, not the names of
        // processes it may close. Only our two executables should be checked.
        Assert.Equal("yes", directives["CloseApplications"], ignoreCase: true);
        Assert.Equal("FilesMate.App.exe,FilesMate.SearchHost.exe", directives["CloseApplicationsFilter"]);
        Assert.Equal("no", directives["RestartApplications"], ignoreCase: true);
    }
}
