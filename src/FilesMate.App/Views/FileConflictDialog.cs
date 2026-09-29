using FilesMate.App.Localization;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

internal static class FileConflictDialog
{
    internal const double FooterInset = 8;
    internal sealed class Session { public bool DecideIndividually; }

    public static FileConflictResolver For(FrameworkElement host)
    {
        var session = new Session();
        return (conflict, token) =>
        {
            var completion = new TaskCompletionSource<FileConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!host.DispatcherQueue.TryEnqueue(async () =>
            {
                if (token.IsCancellationRequested || !host.IsLoaded)
                { completion.TrySetResult(new(FileConflictAction.Cancel)); return; }
                try { completion.TrySetResult(await ShowAsync(host, conflict, session, token)); }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetResult(new(FileConflictAction.Cancel));
            return completion.Task;
        };
    }

    private static async Task<FileConflictChoice> ShowAsync(FrameworkElement host, FileConflict conflict, Session session, CancellationToken token)
    {
        var dialog = new ContentDialog
        {
            DefaultButton = ContentDialogButton.None, XamlRoot = host.XamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Max(280, Math.Min(1040, host.XamlRoot.Size.Width - 32));
        dialog.Resources["ContentDialogMinWidth"] = 0d;
        dialog.Resources["ContentDialogMinHeight"] = 0d;
        dialog.Resources["ContentDialogPadding"] = new Thickness(16, 16, 16, FooterInset);
        // Our footer owns its divider; the stock command-area divider adds an extra bottom inset.
        dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
        ContentDialogTheme.Apply(dialog, host);
        await using var body = new FileConflictBody(dialog, host, conflict, session, token);
        dialog.Content = body;
        dialog.Opened += (_, _) => host.DispatcherQueue.TryEnqueue(body.FocusCancel);
        if (session.DecideIndividually && !conflict.CanMerge && !conflict.IsSameItem) body.Compare();
        using var cancellation = token.Register(() => host.DispatcherQueue.TryEnqueue(dialog.Hide));
        RoutedEventHandler unload = (_, _) => dialog.Hide();
        host.Unloaded += unload;
        try { if (!token.IsCancellationRequested) await dialog.ShowAsync(); }
        finally { host.Unloaded -= unload; }
        var choice = token.IsCancellationRequested || !host.IsLoaded ? new(FileConflictAction.Cancel) : body.Choice;
        // Release file previews and read guards before the confirmation or transfer continues.
        await body.DisposeAsync();
        if (choice.Action != FileConflictAction.ReplaceWithoutUndo) return choice;
        var confirm = new ContentDialog
        {
            Title = StringTable.Get("Backup_ReplaceWithoutUndo"),
            Content = new TextBlock { Text = StringTable.Get("Backup_DestructiveHint"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = StringTable.Get("Backup_ReplaceWithoutUndo"), CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close, XamlRoot = host.XamlRoot,
        };
        ContentDialogTheme.Apply(confirm, host);
        using var cancelConfirm = token.Register(() => host.DispatcherQueue.TryEnqueue(confirm.Hide));
        RoutedEventHandler unloadConfirm = (_, _) => confirm.Hide();
        host.Unloaded += unloadConfirm;
        try
        {
            return !token.IsCancellationRequested && host.IsLoaded && await confirm.ShowAsync() == ContentDialogResult.Primary
                ? new(FileConflictAction.ReplaceWithoutUndo) : new(FileConflictAction.Cancel);
        }
        finally { host.Unloaded -= unloadConfirm; }
    }
}
