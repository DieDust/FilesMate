using Microsoft.Win32;
using System.Runtime.Versioning;

namespace FilesMate.Platform.Windows.Processes;

/// <summary>Optional integration with the user's installed WinMerge; never installs or bundles it.</summary>
[SupportedOSPlatform("windows")]
public static class ExternalFileComparison
{
    public static string? FindWinMerge()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinMergeU.exe");
                if (key?.GetValue(null) is string path && Valid(path.Trim('"'))) return path.Trim('"');
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var path = Path.Combine(root, "WinMerge", "WinMergeU.exe");
            if (Valid(path)) return path;
        }
        return null;
    }

    private static bool Valid(string path) => Path.IsPathFullyQualified(path) && File.Exists(path)
        && string.Equals(Path.GetFileName(path), "WinMergeU.exe", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Arguments(string incoming, string existing) =>
        ["/e", "/u", "/wl", "/wr", "/s-", Path.GetFullPath(incoming), Path.GetFullPath(existing)];

    public static void Open(string executable, string incoming, string existing) =>
        DetachedProcess.Start(executable, Arguments(incoming, existing));
}
