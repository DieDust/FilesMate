using System.Runtime.Versioning;

using FilesMate.Platform.Windows.Associations;

namespace FilesMate.Platform.Windows.Tests.Associations;

public sealed class DefaultFolderAssociationTests
{
    [Fact]
    public void Uninstall_restores_handlers_owned_by_the_installation()
    {
        var registry = new MemoryUserRegistry();
        var key = DefaultFolderAssociation.ClassesKey("Directory");
        registry.SetDefaultValue(key, "explorer.exe");
        registry.SetValue(key, DefaultFolderAssociation.DelegateExecuteName, "stock-handler");
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";
        association.Enable(exe);

        association.UnregisterInstallation(exe.ToUpperInvariant());

        Assert.Equal("explorer.exe", registry.GetDefaultValue(key));
        Assert.Equal("stock-handler", registry.GetValue(key, DefaultFolderAssociation.DelegateExecuteName));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder")));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.ExplorerAppPathsKey));
        Assert.Null(registry.GetValue(DefaultFolderAssociation.ExplorerAppPathsKey, "Path"));
        Assert.Equal(2, registry.NotifyCount);
    }

    [Fact]
    public void Uninstall_leaves_another_installation_and_its_backups_untouched()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string otherExe = @"D:\Development\FilesMate.App.exe";
        association.Enable(otherExe);

        association.UnregisterInstallation(@"C:\Apps\FilesMate.App.exe");

        Assert.True(association.IsEnabled(otherExe));
        Assert.Equal(DefaultFolderAssociation.MissingMarker,
            registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Directory")));
        Assert.Equal(1, registry.NotifyCount);
    }

    [Fact]
    public void Uninstall_preserves_handlers_changed_after_installation()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";
        association.Enable(exe);
        var key = DefaultFolderAssociation.ClassesKey("Directory");
        registry.SetDefaultValue(key, "another-manager.exe");
        registry.SetValue(key, DefaultFolderAssociation.DelegateExecuteName, "another-handler");

        association.UnregisterInstallation(exe);

        Assert.Equal("another-manager.exe", registry.GetDefaultValue(key));
        Assert.Equal("another-handler", registry.GetValue(key, DefaultFolderAssociation.DelegateExecuteName));
        Assert.Equal(DefaultFolderAssociation.MissingMarker,
            registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Directory")));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder")));
    }

    [Fact]
    public void Win_e_class_matches_files_and_explorerplusplus()
    {
        Assert.Equal(
            @"CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}",
            DefaultFolderAssociation.WinEClass);
        Assert.DoesNotContain("447b-9315", DefaultFolderAssociation.WinEClass, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enable_matches_files_folder_open_explore_and_win_e()
    {
        var registry = new MemoryUserRegistry();
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory"), "explorer.exe \"%1\"");
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Directory"),
            DefaultFolderAssociation.DelegateExecuteName,
            "{11dbb47c-a525-400b-9e80-a54615a090c0}");
        registry.SetDefaultValue(
            DefaultFolderAssociation.CommandKey("Directory", "explore"),
            "explorer.exe /e,\"%1\"");
        registry.SetDefaultValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            @"C:\Windows\Explorer.exe");
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName,
            "{11dbb47c-a525-400b-9e80-a54615a090c0}");
        registry.SetValue(
            DefaultFolderAssociation.CommandKey("Folder", "explore"),
            DefaultFolderAssociation.DelegateExecuteName,
            "{11dbb47c-a525-400b-9e80-a54615a090c0}");
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";

        association.Enable(exe);

        var command = DefaultFolderAssociation.FormatCommand(exe);
        Assert.True(association.IsEnabled(exe));
        Assert.Equal(1, registry.NotifyCount);
        Assert.Equal(command, registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory")));
        Assert.Equal(command, registry.GetDefaultValue(DefaultFolderAssociation.CommandKey("Directory", "explore")));
        Assert.Equal(command, registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Drive")));
        Assert.Equal(command, registry.GetDefaultValue(DefaultFolderAssociation.CommandKey("Drive", "explore")));
        Assert.Equal(command, registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder")));
        Assert.Equal(command, registry.GetDefaultValue(DefaultFolderAssociation.CommandKey("Folder", "explore")));
        Assert.Equal(exe, registry.GetDefaultValue(DefaultFolderAssociation.ExplorerAppPathsKey));
        Assert.Equal(string.Empty, registry.GetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName));
        Assert.Equal(string.Empty, registry.GetValue(
            DefaultFolderAssociation.CommandKey("Folder", "explore"),
            DefaultFolderAssociation.DelegateExecuteName));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "opennewwindow")));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "open")));
        Assert.Equal("explorer.exe \"%1\"", registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Directory")));
        Assert.Equal(DefaultFolderAssociation.MissingMarker, registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Drive")));
        Assert.Equal(@"C:\Windows\Explorer.exe", registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Folder")));
        Assert.Equal(
            "{11dbb47c-a525-400b-9e80-a54615a090c0}",
            registry.GetValue(
                DefaultFolderAssociation.BackupKey("Folder"),
                DefaultFolderAssociation.DelegateExecuteName));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.LegacyWinEClass, "opennewwindow")));
    }

    [Fact]
    public void Enable_stamps_delegate_execute_when_the_command_is_already_ours()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\FilesMate.exe";
        var command = DefaultFolderAssociation.FormatCommand(exe);
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory"), command);
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Drive"), command);

        Assert.False(association.IsEnabled(exe));
        Assert.True(association.HasOurCommand(exe));

        association.Enable(exe);

        Assert.True(association.IsEnabled(exe));
        Assert.Equal(string.Empty, registry.GetValue(
            DefaultFolderAssociation.ClassesKey("Directory"),
            DefaultFolderAssociation.DelegateExecuteName));
        Assert.Equal(string.Empty, registry.GetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName));
    }

    [Fact]
    public void Enable_clears_legacy_and_current_win_e_overlays()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";
        var winE = DefaultFolderAssociation.FormatCommand(exe, includeItem: false);
        registry.SetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.LegacyWinEClass, "opennewwindow"),
            winE);
        registry.SetValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.LegacyWinEClass, "opennewwindow"),
            DefaultFolderAssociation.DelegateExecuteName,
            string.Empty);
        registry.SetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "opennewwindow"),
            winE);
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory"), DefaultFolderAssociation.FormatCommand(exe));

        association.Enable(exe);

        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "opennewwindow")));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.LegacyWinEClass, "opennewwindow")));
        Assert.True(association.IsEnabled(exe));
    }

    [Fact]
    public void Enable_overlays_folder_open_used_by_qq_and_shellexecute()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string current = @"C:\New\FilesMate.App.exe";
        var stolen = DefaultFolderAssociation.FormatCommand(@"D:\Old\FilesMate.App.exe");
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder"), stolen);
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName,
            string.Empty);
        registry.SetDefaultValue(DefaultFolderAssociation.BackupKey("Folder"), @"C:\Windows\Explorer.exe");
        registry.SetValue(
            DefaultFolderAssociation.BackupKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName,
            "{11dbb47c-a525-400b-9e80-a54615a090c0}");

        association.Enable(current);

        Assert.Equal(
            DefaultFolderAssociation.FormatCommand(current),
            registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder")));
        Assert.Equal(
            string.Empty,
            registry.GetValue(
                DefaultFolderAssociation.ClassesKey("Folder"),
                DefaultFolderAssociation.DelegateExecuteName));
        Assert.Equal(
            @"C:\Windows\Explorer.exe",
            registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Folder")));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "opennewwindow")));
    }

    [Fact]
    public void Disable_restores_backup_and_removes_keys_we_created()
    {
        var registry = new MemoryUserRegistry();
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory"), "previous \"%1\"");
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Directory"),
            DefaultFolderAssociation.DelegateExecuteName,
            "{11dbb47c-a525-400b-9e80-a54615a090c0}");
        registry.SetDefaultValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            @"C:\Windows\Explorer.exe");
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName,
            "{11dbb47c-a525-400b-9e80-a54615a090c0}");
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"D:\FilesMate\FilesMate.App.exe";

        association.Enable(exe);
        association.Disable(exe);

        Assert.False(association.IsEnabled(exe));
        Assert.Equal(2, registry.NotifyCount);
        Assert.Equal("previous \"%1\"", registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory")));
        Assert.Equal(
            "{11dbb47c-a525-400b-9e80-a54615a090c0}",
            registry.GetValue(
                DefaultFolderAssociation.ClassesKey("Directory"),
                DefaultFolderAssociation.DelegateExecuteName));
        Assert.Equal(
            @"C:\Windows\Explorer.exe",
            registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder")));
        Assert.Equal(
            "{11dbb47c-a525-400b-9e80-a54615a090c0}",
            registry.GetValue(
                DefaultFolderAssociation.ClassesKey("Folder"),
                DefaultFolderAssociation.DelegateExecuteName));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Drive")));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "opennewwindow")));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "open")));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Directory")));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Drive")));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Folder")));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.ExplorerAppPathsKey));
    }

    [Fact]
    public void Disable_leaves_user_changes_alone()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\FilesMate.exe";
        association.Enable(exe);
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory"), "other.exe \"%1\"");

        association.Disable(exe);

        Assert.Equal("other.exe \"%1\"", registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Directory")));
        Assert.Null(registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Directory")));
    }

    [Fact]
    public void Enable_clears_leftover_file_explorer_clsid_open()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";
        var stolen = DefaultFolderAssociation.FormatCommand(exe, includeItem: false);
        registry.SetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "open"),
            stolen);
        registry.SetValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "open"),
            DefaultFolderAssociation.DelegateExecuteName,
            string.Empty);

        association.Enable(exe);

        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "open")));
        Assert.Null(registry.GetValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "open"),
            DefaultFolderAssociation.DelegateExecuteName));
        Assert.Null(registry.GetDefaultValue(
            DefaultFolderAssociation.CommandKey(DefaultFolderAssociation.WinEClass, "opennewwindow")));
    }

    [Fact]
    public void RunWhileSuspended_restores_explorer_during_the_action_then_reapplies()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";
        registry.SetDefaultValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            @"C:\Windows\Explorer.exe");
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName,
            "{11dbb47c-a525-400b-9e80-a54615a090c0}");
        association.Enable(exe);

        string? folderDuring = null;
        association.RunWhileSuspended(exe, () =>
        {
            folderDuring = registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder"));
        });

        Assert.Equal(@"C:\Windows\Explorer.exe", folderDuring);
        Assert.True(association.IsEnabled(exe));
        Assert.Equal(
            DefaultFolderAssociation.FormatCommand(exe),
            registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder")));
    }

    [Fact]
    public void Enable_does_not_backup_filesmate_as_the_explorer_handler()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";
        registry.SetDefaultValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.FormatCommand(exe));
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName,
            string.Empty);

        association.Enable(exe);

        Assert.Equal(
            DefaultFolderAssociation.MissingMarker,
            registry.GetDefaultValue(DefaultFolderAssociation.BackupKey("Folder")));
        Assert.Equal(
            DefaultFolderAssociation.MissingMarker,
            registry.GetValue(
                DefaultFolderAssociation.BackupKey("Folder"),
                DefaultFolderAssociation.DelegateExecuteName));
    }

    [Fact]
    public void RunWhileSuspended_stamps_stock_explorer_when_backup_is_ourselves()
    {
        var registry = new MemoryUserRegistry();
        var association = new DefaultFolderAssociation(registry);
        const string exe = @"C:\Apps\FilesMate.App.exe";
        var stolen = DefaultFolderAssociation.FormatCommand(exe);
        registry.SetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder"), stolen);
        registry.SetValue(
            DefaultFolderAssociation.ClassesKey("Folder"),
            DefaultFolderAssociation.DelegateExecuteName,
            string.Empty);
        registry.SetDefaultValue(DefaultFolderAssociation.BackupKey("Folder"), stolen);

        string? folderDuring = null;
        string? executeDuring = null;
        association.RunWhileSuspended(exe, () =>
        {
            folderDuring = registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder"));
            executeDuring = registry.GetValue(
                DefaultFolderAssociation.ClassesKey("Folder"),
                DefaultFolderAssociation.DelegateExecuteName);
        });

        Assert.Contains("explorer.exe", folderDuring, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FilesMate.App.exe", folderDuring, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            DefaultFolderAssociation.ExplorerDelegateExecute,
            executeDuring,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(stolen, registry.GetDefaultValue(DefaultFolderAssociation.ClassesKey("Folder")));
    }
}

[SupportedOSPlatform("windows")]
public sealed class ClassicExplorerTests
{
    [Fact]
    public void Launch_starts_explorer_exe_without_shellexecute()
    {
        var info = ClassicExplorer.CreateStartInfo();
        Assert.Equal(ClassicExplorer.ExplorerFileName, Path.GetFileName(info.FileName), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ClassicExplorer.SeparateArguments, info.Arguments);
        Assert.Equal("/n,/separate", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.Contains("/separate", info.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20D04FE0", info.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/e,", info.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("opennewprocess", info.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ClassicExplorer.ThisPc, info.Arguments, StringComparison.Ordinal);
    }
}
