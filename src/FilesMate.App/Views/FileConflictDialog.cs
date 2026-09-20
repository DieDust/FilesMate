using FilesMate.App.Localization;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

internal static class FileConflictDialog
{
    public static FileConflictResolver For(FrameworkElement host) => (conflict, token) =>
    {
        var completion = new TaskCompletionSource<FileConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!host.DispatcherQueue.TryEnqueue(async () =>
        {
            if (token.IsCancellationRequested || !host.IsLoaded)
            { completion.TrySetResult(new(FileConflictAction.Cancel)); return; }
            try { completion.TrySetResult(await ShowAsync(host, conflict, token)); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetResult(new(FileConflictAction.Cancel));
        return completion.Task;
    };

    private static async Task<FileConflictChoice> ShowAsync(FrameworkElement host, FileConflict conflict, CancellationToken token)
    {
        var content = new StackPanel { Spacing = 12, MinWidth = 320, MaxWidth = 480 };
        content.Children.Add(new TextBlock { Text = Path.GetFileName(conflict.Destination), FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var comparison = new Grid { ColumnSpacing = 20 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition());
        comparison.ColumnDefinitions.Add(new ColumnDefinition());
        void Details(int column, string label, string path, FileConflictDetails? details, FileConflictDetails? other)
        {
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(new TextBlock { Text = StringTable.Get(label), FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var location = new TextBlock { Text = path, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 };
            ToolTipService.SetToolTip(location, path);
            panel.Children.Add(location);
            if (details is not null)
            {
                panel.Children.Add(new TextBlock { Text = StringTable.Format("Transfer_Bytes", details.Length.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = details.LastWriteTimeUtc.ToLocalTime().ToString("G", System.Globalization.CultureInfo.CurrentCulture), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                if (other is not null && details.LastWriteTimeUtc > other.LastWriteTimeUtc)
                    panel.Children.Add(new TextBlock { Text = StringTable.Get("Transfer_Newer"), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            }
            Grid.SetColumn(panel, column); comparison.Children.Add(panel);
        }
        if (conflict.IsSameItem)
        {
            content.Children.Add(new TextBlock { Text = StringTable.Get("Transfer_SameItemHint"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            var location = new TextBlock { Text = conflict.Source, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 };
            ToolTipService.SetToolTip(location, conflict.Source); content.Children.Add(location);
        }
        else
        {
            Details(0, "Transfer_IncomingLabel", conflict.Source, conflict.Incoming, conflict.Existing);
            Details(1, "Transfer_ExistingLabel", conflict.Destination, conflict.Existing, conflict.Incoming);
            content.Children.Add(comparison);
            if (!conflict.CanMerge && !conflict.CanReplace)
                content.Children.Add(new TextBlock { Text = StringTable.Get(conflict.DestinationIsLink ? "Transfer_LinkHint" : "Transfer_TypeHint"),
                    FontSize = 13, TextWrapping = TextWrapping.Wrap });
        }
        var options = new StackPanel { Spacing = 0 };
        if (conflict.CanReplace)
            content.Children.Add(new TextBlock
            {
                Text = StringTable.Format(conflict.BackupUnavailable ? "Backup_LimitHint" : "Backup_ReservationHint",
                    (conflict.BackupBytes / 1048576d).ToString("0.##"), (conflict.BackupUsedBytes / 1048576d).ToString("0.##")),
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
            });
        content.Children.Add(options);
        RadioButton Option(string key, FileConflictAction action)
        {
            var option = new RadioButton { Tag = action, MinHeight = 32, Padding = new Thickness(8, 0, 0, 0), Margin = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = StringTable.Get(key), FontSize = 14, TextWrapping = TextWrapping.Wrap } };
            options.Children.Add(option); return option;
        }
        if (conflict.CanMerge) Option("Transfer_Merge", FileConflictAction.Merge);
        if (conflict.CanReplace) Option(conflict.BackupUnavailable ? "Backup_ReplaceWithoutUndo" : "Transfer_Replace",
            conflict.BackupUnavailable ? FileConflictAction.ReplaceWithoutUndo : FileConflictAction.Replace);
        Option("Files_Skip", FileConflictAction.Skip).IsChecked = true;
        Option(conflict.IsSameItem ? "Transfer_CreateCopy" : "Transfer_KeepBothButton", FileConflictAction.KeepBoth);
        var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8, Visibility = Visibility.Collapsed };
        content.Children.Add(explanation);
        var all = new CheckBox { MinHeight = 32, Padding = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, Content = new TextBlock {
            Text = StringTable.Get("Transfer_ApplyAllCompact"), FontSize = 13, TextWrapping = TextWrapping.Wrap } };
        content.Children.Add(all);
        var dialog = new ContentDialog
        {
            Title = StringTable.Get(conflict.IsSameItem ? "Transfer_SameItemTitle" : conflict.CanMerge ? "Files_FolderConflict" : "Files_NameConflict"),
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Clamp(host.XamlRoot.Size.Height - 220, 160, 480) },
            PrimaryButtonText = StringTable.Get("Files_Skip"), CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary, XamlRoot = host.XamlRoot,
        };
        ContentDialogTheme.Apply(dialog, host);
        foreach (var option in options.Children.OfType<RadioButton>())
        {
            void Select()
            {
                var action = (FileConflictAction)option.Tag;
                explanation.Text = action switch
                {
                    FileConflictAction.KeepBoth => StringTable.Format("Transfer_NumberedName", conflict.NumberedName),
                    FileConflictAction.Replace => StringTable.Get("Transfer_ReplaceHint"),
                    FileConflictAction.ReplaceWithoutUndo => StringTable.Get("Backup_DestructiveHint"),
                    _ => string.Empty,
                };
                explanation.Visibility = explanation.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                dialog.PrimaryButtonText = StringTable.Get(action switch
                {
                    FileConflictAction.Replace => "Transfer_ReplaceButton",
                    FileConflictAction.ReplaceWithoutUndo => "Backup_ReplaceWithoutUndo",
                    FileConflictAction.KeepBoth => conflict.IsSameItem ? "Transfer_CreateCopy" : "Transfer_KeepBothButton",
                    FileConflictAction.Merge => "Files_Merge",
                    _ => "Files_Skip",
                });
                all.IsEnabled = action != FileConflictAction.ReplaceWithoutUndo;
                if (!all.IsEnabled) all.IsChecked = false;
                dialog.DefaultButton = action == FileConflictAction.ReplaceWithoutUndo ? ContentDialogButton.Close : ContentDialogButton.Primary;
            }
            option.Checked += (_, _) => Select();
            option.Tapped += (_, _) => Select();
        }
        using var cancellation = token.Register(() => host.DispatcherQueue.TryEnqueue(dialog.Hide));
        RoutedEventHandler unload = (_, _) => dialog.Hide();
        host.Unloaded += unload;
        try
        {
            if (token.IsCancellationRequested || await dialog.ShowAsync() != ContentDialogResult.Primary) return new(FileConflictAction.Cancel);
            var selected = options.Children.OfType<RadioButton>().Single(r => r.IsChecked == true);
            var action = (FileConflictAction)selected.Tag;
            if (action == FileConflictAction.ReplaceWithoutUndo)
            {
                // Never let a remembered bulk rule authorize an irreversible replacement.
                var confirm = new ContentDialog
                {
                    Title = StringTable.Get("Backup_ReplaceWithoutUndo"),
                    Content = new TextBlock { Text = StringTable.Get("Backup_DestructiveHint"), TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = StringTable.Get("Backup_ReplaceWithoutUndo"), CloseButtonText = StringTable.Get("Cancel"),
                    DefaultButton = ContentDialogButton.Close, XamlRoot = host.XamlRoot,
                };
                ContentDialogTheme.Apply(confirm, host);
                using var cancelConfirm = token.Register(() => host.DispatcherQueue.TryEnqueue(confirm.Hide));
                RoutedEventHandler closeConfirm = (_, _) => confirm.Hide();
                host.Unloaded += closeConfirm;
                try
                {
                    if (token.IsCancellationRequested || !host.IsLoaded || await confirm.ShowAsync() != ContentDialogResult.Primary)
                        return new(FileConflictAction.Cancel);
                }
                finally { host.Unloaded -= closeConfirm; }
            }
            return new(action, action != FileConflictAction.ReplaceWithoutUndo && all.IsChecked == true);
        }
        finally { host.Unloaded -= unload; }
    }
}
