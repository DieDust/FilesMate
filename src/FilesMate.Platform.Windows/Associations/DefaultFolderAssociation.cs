namespace FilesMate.Platform.Windows.Associations;

public sealed class DefaultFolderAssociation
{
    public const string MissingMarker = "__filesmate_missing__";
    public const string BackupRoot = @"Software\FilesMate\DefaultFolderHandler";
    public const string DelegateExecuteName = "DelegateExecute";

    /// <summary>
    /// Windows "File Explorer" / Win+E CLSID. Same id Files and Explorer++ overlay.
    /// </summary>
    public const string WinEClass = @"CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";

    public const string LegacyWinEClass = @"CLSID\{52205fd8-5dfb-447b-9315-f8f65da153c3}";
    public const string FilesMateExeName = "FilesMate.App.exe";
    public const string ExplorerDelegateExecute = "{11dbb47c-a525-400b-9e80-a54615a090c0}";
    public const string ExplorerAppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\explorer.exe";
    public const string ExplorerAppPathsBackup = BackupRoot + @"\AppPathsExplorer";

    private static readonly Handler[] Handlers =
    [
        new("Directory", "open", IncludeItem: true),
        new("Directory", "explore", IncludeItem: true),
        new("Drive", "open", IncludeItem: true),
        new("Drive", "explore", IncludeItem: true),
        // QQ, desktop, and ShellExecute hit Folder, not Directory.
        new("Folder", "open", IncludeItem: true),
        new("Folder", "explore", IncludeItem: true),
    ];

    private readonly IUserRegistry _registry;

    public DefaultFolderAssociation(IUserRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public static string FormatCommand(string executable, bool includeItem = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var exe = executable.Trim().Trim('"');
        return includeItem ? $"\"{exe}\" /open \"%1\"" : $"\"{exe}\"";
    }

    public static string ClassesKey(string className) => CommandKey(className, "open");

    public static string CommandKey(string className, string verb) =>
        $@"Software\Classes\{className}\shell\{verb}\command";

    public static string BackupKey(string className) => $@"{BackupRoot}\{className}";

    public static string BackupKey(string className, string verb) =>
        className is "Directory" or "Drive" or "Folder" && verb == "open"
            ? BackupKey(className)
            : $@"{BackupRoot}\{className}\{verb}";

    public bool IsEnabled(string executable)
    {
        foreach (var handler in Handlers)
        {
            var key = CommandKey(handler.ClassName, handler.Verb);
            if (!string.Equals(
                    _registry.GetDefaultValue(key),
                    FormatCommand(executable, handler.IncludeItem),
                    StringComparison.OrdinalIgnoreCase)
                || _registry.GetValue(key, DelegateExecuteName) is not "")
            {
                return false;
            }
        }

        return string.Equals(
            _registry.GetDefaultValue(ExplorerAppPathsKey),
            executable.Trim().Trim('"'),
            StringComparison.OrdinalIgnoreCase);
    }

    public bool HasOurCommand(string executable) =>
        IsFilesMateCommand(_registry.GetDefaultValue(ClassesKey("Folder")), executable, includeItem: true)
        || IsFilesMateCommand(_registry.GetDefaultValue(ClassesKey("Directory")), executable, includeItem: true)
        || IsFilesMateCommand(
            _registry.GetDefaultValue(CommandKey(WinEClass, "opennewwindow")),
            executable,
            includeItem: false)
        || IsFilesMateCommand(
            _registry.GetDefaultValue(CommandKey(LegacyWinEClass, "opennewwindow")),
            executable,
            includeItem: false)
        || IsFilesMateCommand(
            _registry.GetDefaultValue(ExplorerAppPathsKey),
            executable,
            includeItem: false);

    public void Enable(string executable)
    {
        foreach (var handler in Handlers)
        {
            Apply(handler, executable);
        }

        ApplyExplorerAppPath(executable);
        RemoveExplorerHostOverlays();
        _registry.NotifyAssociationsChanged();
    }

    public void Disable(string executable)
    {
        foreach (var handler in Handlers)
        {
            RestoreHandler(handler, executable);
        }

        RestoreExplorerAppPath(executable);
        RemoveExplorerHostOverlays();
        _registry.NotifyAssociationsChanged();
    }

    /// <summary>Remove only registrations owned by this installed executable.</summary>
    public void UnregisterInstallation(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var exe = executable.Trim().Trim('"');
        var changed = false;
        foreach (var handler in Handlers.Concat(new[]
        {
            new Handler(LegacyWinEClass, "opennewwindow", false),
            new Handler(WinEClass, "opennewwindow", false),
            new Handler(WinEClass, "open", false),
        }))
        {
            var key = CommandKey(handler.ClassName, handler.Verb);
            if (string.Equals(_registry.GetDefaultValue(key),
                FormatCommand(exe, handler.IncludeItem), StringComparison.OrdinalIgnoreCase))
            {
                RestoreFromBackup(key, BackupKey(handler.ClassName, handler.Verb));
                changed = true;
            }
        }

        if (string.Equals(_registry.GetDefaultValue(ExplorerAppPathsKey), exe,
            StringComparison.OrdinalIgnoreCase))
        {
            RestoreFromBackup(ExplorerAppPathsKey, ExplorerAppPathsBackup);
            _registry.DeleteValue(ExplorerAppPathsKey, "Path");
            changed = true;
        }

        if (changed)
        {
            _registry.NotifyAssociationsChanged();
        }
    }

    public void RunWhileSuspended(string executable, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(action);
        var resume = HasOurCommand(executable);
        if (resume)
        {
            Disable(executable);
            InstallStockExplorerFolder();
        }

        try
        {
            action();
        }
        finally
        {
            if (resume)
            {
                Enable(executable);
            }
        }
    }

    private void RemoveExplorerHostOverlays()
    {
        RemoveOverlay(LegacyWinEClass, "opennewwindow");
        RemoveOverlay(WinEClass, "opennewwindow");
        RemoveOverlay(WinEClass, "open");
    }

    private void Apply(Handler handler, string executable)
    {
        var key = CommandKey(handler.ClassName, handler.Verb);
        var backup = BackupKey(handler.ClassName, handler.Verb);
        var command = FormatCommand(executable, handler.IncludeItem);
        if (_registry.GetDefaultValue(backup) is null)
        {
            _registry.SetDefaultValue(
                backup,
                BackupCommand(_registry.GetDefaultValue(key), executable, handler.IncludeItem));
        }

        if (_registry.GetValue(backup, DelegateExecuteName) is null)
        {
            var current = _registry.GetValue(key, DelegateExecuteName);
            _registry.SetValue(
                backup,
                DelegateExecuteName,
                string.IsNullOrEmpty(current) ? MissingMarker : current);
        }

        _registry.SetDefaultValue(key, command);
        _registry.SetValue(key, DelegateExecuteName, string.Empty);
    }

    // QQ and other apps call explorer.exe /select,file. App Paths catches the
    // unqualified explorer.exe name; a full C:\Windows\explorer.exe path still
    // reaches stock Explorer.
    private void ApplyExplorerAppPath(string executable)
    {
        var exe = executable.Trim().Trim('"');
        if (_registry.GetDefaultValue(ExplorerAppPathsBackup) is null)
        {
            _registry.SetDefaultValue(
                ExplorerAppPathsBackup,
                BackupCommand(_registry.GetDefaultValue(ExplorerAppPathsKey), exe, includeItem: false));
        }

        _registry.SetDefaultValue(ExplorerAppPathsKey, exe);
        var directory = Path.GetDirectoryName(exe);
        if (!string.IsNullOrEmpty(directory))
        {
            _registry.SetValue(ExplorerAppPathsKey, "Path", directory);
        }
    }

    private void RestoreExplorerAppPath(string executable)
    {
        var current = _registry.GetDefaultValue(ExplorerAppPathsKey);
        if (!string.Equals(current, executable.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase)
            && !IsFilesMateCommand(current, executable, includeItem: false))
        {
            ClearBackup(ExplorerAppPathsBackup);
            return;
        }

        RestoreFromBackup(ExplorerAppPathsKey, ExplorerAppPathsBackup);
        _registry.DeleteValue(ExplorerAppPathsKey, "Path");
    }

    // ponytail: explorer.exe still consults Folder\open after Disable; stamp the stock COM handler so a missing/self backup cannot leave FilesMate in place.
    private void InstallStockExplorerFolder()
    {
        var explorer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");
        foreach (var verb in new[] { "open", "explore" })
        {
            var key = CommandKey("Folder", verb);
            if (FolderLooksLikeStockExplorer(key))
            {
                continue;
            }

            _registry.SetDefaultValue(key, explorer);
            _registry.SetValue(key, DelegateExecuteName, ExplorerDelegateExecute);
        }

        _registry.NotifyAssociationsChanged();
    }

    private bool FolderLooksLikeStockExplorer(string key)
    {
        var command = _registry.GetDefaultValue(key);
        var execute = _registry.GetValue(key, DelegateExecuteName);
        return !string.IsNullOrEmpty(command)
            && command.Contains("explorer.exe", StringComparison.OrdinalIgnoreCase)
            && !IsFilesMateCommand(command, FilesMateExeName, includeItem: true)
            && string.Equals(execute, ExplorerDelegateExecute, StringComparison.OrdinalIgnoreCase);
    }

    private void RestoreHandler(Handler handler, string executable)
    {
        var key = CommandKey(handler.ClassName, handler.Verb);
        var backupKey = BackupKey(handler.ClassName, handler.Verb);
        var current = _registry.GetDefaultValue(key);
        if (!IsFilesMateCommand(current, executable, handler.IncludeItem))
        {
            ClearBackup(backupKey);
            return;
        }

        RestoreFromBackup(key, backupKey);
    }

    private void RemoveOverlay(string className, string verb)
    {
        var key = CommandKey(className, verb);
        var backupKey = BackupKey(className, verb);
        _registry.DeleteValue(key, DelegateExecuteName);
        _registry.DeleteDefaultValue(key);
        ClearBackup(backupKey);
    }

    private void RestoreFromBackup(string key, string backupKey)
    {
        var backup = _registry.GetDefaultValue(backupKey);
        if (IsMissingBackup(backup))
        {
            _registry.DeleteValue(key, DelegateExecuteName);
            _registry.DeleteDefaultValue(key);
            ClearBackup(backupKey);
            return;
        }

        Restore(key, backup, string.Empty);
        Restore(key, _registry.GetValue(backupKey, DelegateExecuteName), DelegateExecuteName);
        ClearBackup(backupKey);
    }

    private void ClearBackup(string backupKey)
    {
        _registry.DeleteValue(backupKey, DelegateExecuteName);
        _registry.DeleteDefaultValue(backupKey);
    }

    private void Restore(string key, string? backup, string name)
    {
        if (IsMissingBackup(backup))
        {
            _registry.DeleteValue(key, name);
            return;
        }

        _registry.SetValue(key, name, backup!);
    }

    private static bool IsFilesMateCommand(string? command, string executable, bool includeItem)
    {
        if (string.IsNullOrEmpty(command))
        {
            return false;
        }

        return command.Contains(FilesMateExeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                command,
                FormatCommand(executable, includeItem),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string BackupCommand(string? current, string executable, bool includeItem) =>
        string.IsNullOrEmpty(current) || IsFilesMateCommand(current, executable, includeItem)
            ? MissingMarker
            : current;

    private static bool IsMissingBackup(string? backup) =>
        string.IsNullOrEmpty(backup)
        || string.Equals(backup, MissingMarker, StringComparison.Ordinal)
        || backup.Contains(FilesMateExeName, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Handler(string ClassName, string Verb, bool IncludeItem);
}
