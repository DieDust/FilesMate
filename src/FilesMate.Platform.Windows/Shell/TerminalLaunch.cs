using System.Diagnostics;

namespace FilesMate.Platform.Windows.Shell;

public static class TerminalLaunch
{
    public static void Open(string folder)
    {
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("请选择一个可访问的文件夹。");
        try { using var process = Process.Start(CreateStartInfo(folder)); }
        catch (System.ComponentModel.Win32Exception)
        { using var process = Process.Start(CreateStartInfo(folder, fallback: true)); }
    }

    public static ProcessStartInfo CreateStartInfo(string folder, bool fallback = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var start = new ProcessStartInfo
        {
            FileName = fallback
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe"),
            UseShellExecute = true,
            WorkingDirectory = folder,
        };
        if (fallback)
        {
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NoExit");
            start.ArgumentList.Add("-Command");
            // Only base64 data enters the script, including for smart quotes and newlines.
            var encodedPath = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(folder));
            start.ArgumentList.Add("Set-Location -LiteralPath ([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + encodedPath + "')))");
            start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        }
        else
        {
            start.ArgumentList.Add("-d");
            // Terminal treats semicolons as subcommand separators. Pass the directory
            // through the process working directory, never through its command parser.
            start.ArgumentList.Add(".");
        }

        return start;
    }
}
