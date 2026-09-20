using Microsoft.Win32;

namespace FilesMate.Search;

/// <summary>Per-user startup of the search listener; never launches the file-manager UI.</summary>
public static class GlobalSearchStartup
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "FilesMate.GlobalSearch";

    public static string Command(string host, string manager)
    {
        static string Quote(string path)
        {
            if (path.IndexOfAny(['"', '\r', '\n']) >= 0) throw new ArgumentException("启动路径无效。");
            return "\"" + Path.GetFullPath(path) + "\"";
        }
        var siblingManager = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(host))!, "..", "FilesMate.App.exe"));
        var command = $"{Quote(host)} --background --startup";
        if (!string.Equals(Path.GetFullPath(manager), siblingManager, StringComparison.OrdinalIgnoreCase)) command += $" --manager {Quote(manager)}";
        if (command.Length > 260) throw new ArgumentException("安装路径过长，请选择更短的位置以启用开机自启。");
        return command;
    }

    public static string HostForManager(string manager) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manager))!, "SearchHost", "FilesMate.SearchHost.exe");
    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    private static bool Applicable(string? profile, string registryPath) => OperatingSystem.IsWindows() &&
        (registryPath != RunKey || string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile ?? GlobalSearchConfiguration.DefaultDirectory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(GlobalSearchConfiguration.DefaultDirectory)), StringComparison.OrdinalIgnoreCase));

    public static void Synchronize(GlobalSearchSettings settings, string host, string manager, string? profile = null) =>
        Synchronize(settings, host, manager, profile, RunKey);
    internal static void Synchronize(GlobalSearchSettings settings, string host, string manager, string? profile, string registryPath)
    {
        if (!Applicable(profile, registryPath)) return;
        var command = settings.Enabled && settings.StartAtLogin ? Command(host, manager) : null;
        if (command is not null && (!File.Exists(host) || !File.Exists(manager))) throw new IOException("未找到全局搜索组件，请重新安装 FilesMate。");
        Write(command, registryPath);
    }

    public static void Save(GlobalSearchSettings settings, string host, string manager, string? profile = null) =>
        Save(settings, host, manager, profile, RunKey);
    internal static void Save(GlobalSearchSettings settings, string host, string manager, string? profile, string registryPath)
    {
        if (!Applicable(profile, registryPath)) { GlobalSearchConfiguration.Save(settings, profile); return; }
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        var previous = key?.GetValue(ValueName) as string;
        try
        {
            Synchronize(settings, host, manager, profile, registryPath);
            GlobalSearchConfiguration.Save(settings, profile);
        }
        catch
        {
            Write(previous, registryPath);
            throw;
        }
    }

    public static void RemoveOwned(string host, string manager, string? profile = null) => RemoveOwned(host, manager, profile, RunKey);
    internal static void RemoveOwned(string host, string manager, string? profile, string registryPath)
    {
        if (!Applicable(profile, registryPath)) return;
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
        if (string.Equals(key?.GetValue(ValueName) as string, Command(host, manager), StringComparison.OrdinalIgnoreCase)) key!.DeleteValue(ValueName, false);
    }

    /// <summary>
    /// Whether the user (or an administrator) switched our Run entry off under Windows Settings › Apps › Startup.
    /// Windows keeps the Run value and records the veto separately, so the entry looks enabled from the Run key alone.
    /// </summary>
    public static bool IsDisabledByWindows()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            // The first byte is the state: 0x02/0x06 enabled, 0x03/0x07 disabled (the rest is a FILETIME of the change).
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 0x01) != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return false; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Write(string? command, string registryPath)
    {
        // Do not touch StartupApproved: a Windows-level user/admin disable remains effective.
        using var existing = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
        if (string.Equals(existing?.GetValue(ValueName) as string, command, StringComparison.Ordinal)) return;
        if (command is null) { existing?.DeleteValue(ValueName, false); return; }
        using var key = existing is null ? Registry.CurrentUser.CreateSubKey(registryPath) : null;
        (existing ?? key!).SetValue(ValueName, command, RegistryValueKind.String);
    }
}
