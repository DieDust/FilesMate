using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

public sealed class TerminalLaunchTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\My Files\")]
    [InlineData(@"\\server\share\中文 文件")]
    [InlineData(@"C:\a & b\$notes; test")]
    public void Windows_terminal_receives_folder_via_working_directory(string folder)
    {
        var start = TerminalLaunch.CreateStartInfo(folder);
        Assert.EndsWith(@"Microsoft\WindowsApps\wt.exe", start.FileName);
        Assert.Equal(["-d", "."], start.ArgumentList);
        Assert.Empty(start.Arguments);
        Assert.Equal(folder, start.WorkingDirectory);
    }

    [Fact]
    public async Task FallbackActuallyEntersPathWithQuotesAndMetacharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-terminal-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "中文 O'Brien ’ $notes; [data] & test");
        Directory.CreateDirectory(folder);
        try
        {
            var start = TerminalLaunch.CreateStartInfo(folder, fallback: true);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.ArgumentList.Remove("-NoExit");
            start.ArgumentList[^1] += "; [Console]::Write([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes((Get-Location).Path)))";
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(error);
            Assert.Equal(folder, System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(output.Trim())));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Fallback_treats_network_paths_and_metacharacters_as_literals()
    {
        var folder = @"\\server\share\O'Brien $data & test";
        var start = TerminalLaunch.CreateStartInfo(folder, fallback: true);
        Assert.EndsWith(@"WindowsPowerShell\v1.0\powershell.exe", start.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["-NoLogo", "-NoProfile", "-NoExit", "-Command"], start.ArgumentList.Take(4));
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(folder));
        Assert.Contains(encoded, start.ArgumentList[4]);
        Assert.DoesNotContain(folder, start.ArgumentList[4]);
        Assert.Empty(start.Arguments);
    }
}
