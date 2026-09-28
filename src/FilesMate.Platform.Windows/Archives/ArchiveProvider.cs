using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;
using Microsoft.Win32;

namespace FilesMate.Platform.Windows.Archives;

public enum ArchiveProvider { Automatic, BuiltIn, CompactMate, Bandizip, SevenZip, WinRAR, Zip360, HaoZip, SevenZipCompatible, Other }

public static class ArchiveProviderDiscovery
{
    public static string? Find(ArchiveProvider provider, string? configuredPath = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.IsPathFullyQualified(configuredPath) && configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
        if (provider == ArchiveProvider.CompactMate)
            return CompactMateHost.TryFind(new CurrentUserRegistry(), out var compact) ? compact : null;
        foreach (var path in Candidates(provider))
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return Path.GetFullPath(path);
        return null;
    }

    private static IEnumerable<string> Candidates(ArchiveProvider provider)
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var (executables, folders) = provider switch
        {
            ArchiveProvider.Bandizip => (new[] { "Bandizip.exe" }, new[] { "Bandizip" }),
            ArchiveProvider.SevenZip => (new[] { "7zG.exe", "7zFM.exe" }, new[] { "7-Zip" }),
            ArchiveProvider.WinRAR => (new[] { "WinRAR.exe" }, new[] { "WinRAR" }),
            ArchiveProvider.Zip360 => (new[] { "360zip.exe" }, new[] { @"360\360zip", "360zip" }),
            ArchiveProvider.HaoZip => (new[] { "HaoZip.exe" }, new[] { @"2345Soft\HaoZip", "HaoZip" }),
            _ => (Array.Empty<string>(), Array.Empty<string>()),
        };
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var exe in executables)
        {
            var path = RegistryPath(hive, view, @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe, command: false)
                ?? RegistryPath(hive, view, @"Software\Classes\Applications\" + exe + @"\shell\open\command", command: true);
            if (path is not null)
            {
                // Registered 7zFM is a browser; operations belong to its GUI worker.
                if (provider == ArchiveProvider.SevenZip)
                    yield return Path.Combine(Path.GetDirectoryName(path)!, "7zG.exe");
                yield return path;
            }
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
        foreach (var folder in folders)
        foreach (var exe in executables) yield return Path.Combine(root, folder, exe);
    }

    private static string? RegistryPath(RegistryHive hive, RegistryView view, string key, bool command)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var item = root.OpenSubKey(key);
            var value = item?.GetValue(null) as string;
            return command ? CompactMateHost.ParseExecutable(value) : value?.Trim().Trim('"');
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }
}
