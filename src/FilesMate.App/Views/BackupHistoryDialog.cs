using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

internal static class BackupHistoryDialog
{
    internal static string Usage()
    {
        ReplacementBackupBudget.Shared.CanReserve(0); // Refresh the shared ledger without scanning user folders.
        return StringTable.Format("Backup_Usage", (ReplacementBackupBudget.Shared.UsedBytes / 1048576d).ToString("0.##"),
            ReplacementBackupBudget.Shared.Entries.Count);
    }

    internal static async Task ShowAsync(FrameworkElement host)
    {
        var window = App.WindowForElement(host);
        ContentDialog? dialog = null;
        string? openPath = null;
        var panel = new StackPanel { Spacing = 12, MaxWidth = 540 };
        var usage = new TextBlock { Text = Usage(), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(usage);
        panel.Children.Add(new TextBlock { Text = StringTable.Get("Backup_Policy"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = StringTable.Get("Backup_ClearHint"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var paths = new StackPanel { Spacing = 6 };
        foreach (var entry in ReplacementBackupBudget.Shared.Entries)
        {
            var location = new Button { Content = new TextBlock { Text = entry.Directory, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 470 } };
            ToolTipService.SetToolTip(location, entry.Directory);
            location.Click += (_, _) =>
            {
                try
                {
                    if (!Directory.Exists(entry.Directory)) throw new DirectoryNotFoundException(StringTable.Get("Backup_LocationUnavailable"));
                    if (window is null) throw new InvalidOperationException(StringTable.Get("Files_NotReady"));
                    openPath = entry.Directory;
                    dialog!.Hide();
                }
                catch (Exception error) { usage.Text = error.Message; }
            };
            paths.Children.Add(location);
        }
        panel.Children.Add(new ScrollViewer { Content = paths, MaxHeight = 180, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        dialog = new ContentDialog
        {
            Title = StringTable.Get("Backup_Title"), Content = panel,
            PrimaryButtonText = StringTable.Get("Backup_Clear"), CloseButtonText = StringTable.Get("Close"),
            DefaultButton = ContentDialogButton.Close, XamlRoot = host.XamlRoot,
        };
        ContentDialogTheme.Apply(dialog, host);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (FileOperationLifetime.IsBusy) { usage.Text = StringTable.Get("Files_Busy"); return; }
            try
            {
                App.FileUndo.Clear();
                ReplacementBackupBudget.Shared.ForgetMissing();
                usage.Text = Usage() + Environment.NewLine + StringTable.Get("Backup_Cleared");
                foreach (var button in paths.Children.OfType<Button>())
                    button.IsEnabled = ToolTipService.GetToolTip(button) is string path && Directory.Exists(path);
            }
            catch (Exception error) { usage.Text = error.Message; }
            // Residual recovery copies are never deleted solely by their directory name.
        };
        await dialog.ShowAsync();
        if (openPath is not null && window is not null)
        {
            window.CloseSettings();
            window.OpenFolderInNewTab(openPath);
        }
    }
}
