using FilesMate.App.Localization;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private static bool _backupNoticeChecked;
    private void ShowBackupRemnantNotice()
    {
        if (_backupNoticeChecked) return;
        _backupNoticeChecked = true;
        if (ReplacementBackupBudget.Shared.Entries.Count == 0) return;
        var manage = new Button { Content = StringTable.Get("Backup_Manage") };
        manage.Click += async (_, _) => await BackupHistoryDialog.ShowAsync(this);
        var notice = new InfoBar
        {
            Title = StringTable.Get("Backup_Remnants"), Message = BackupHistoryDialog.Usage(),
            IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Informational,
            ActionButton = manage, MaxWidth = 680, Margin = new Thickness(16, 0, 16, 44),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        };
        Canvas.SetZIndex(notice, 32);
        ShellRoot.Children.Add(notice);
        notice.Closed += (_, _) => ShellRoot.Children.Remove(notice);
    }
}
