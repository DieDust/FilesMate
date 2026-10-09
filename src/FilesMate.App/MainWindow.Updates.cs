using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Core.Updates;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private bool _updateDialogOpen;
#if FILESMATE_UI_TEST
    private ContentDialog? _updateDemoDialog;
#endif
    internal bool CanInstallUpdate => !_windowClosed && Tabs.TabItems.OfType<TabViewItem>()
        .All(tab => tab.Tag is not NavigatorTabContent state || state.Navigator?.CanInstallUpdate != false);

    internal void ShowUpdateNotice(UpdateRelease release)
    {
        if (!_windowClosed) QueueAvailableUpdate(release);
    }

    internal async Task DownloadAndInstallUpdateAsync(UpdateRelease release)
    {
        if (_windowClosed || _updateDialogOpen || App.Updates.IsInstalling || Content is not FrameworkElement root) return;
        if (FilesMate.Core.Configuration.RuntimeProfile.IsPortable)
        {
            _updateDialogOpen = true;
            try
            {
                var guidanceBody = new StackPanel { Spacing = 16 };
                guidanceBody.Children.Add(new TextBlock { Text = Loc.Get("Portable_ManualUpdate"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
                guidanceBody.Children.Add(new HyperlinkButton { Content = Loc.Get("Portable_OpenDownloads"),
                    NavigateUri = new Uri("https://github.com/DieDust/FilesMate/releases") });
                var guidance = new ContentDialog { Title = Loc.Get("Portable_UpdateAction"), Content = guidanceBody,
                    CloseButtonText = Loc.Get("Close"), XamlRoot = root.XamlRoot };
                ContentDialogTheme.Apply(guidance, root);
                await guidance.ShowAsync();
            }
            finally { _updateDialogOpen = false; }
            return;
        }
        HideAnnouncementImmediately();
        _updateDialogOpen = true;
        using var cancellation = new CancellationTokenSource();
        var body = new UpdateProgressView { Width = Math.Min(420, Math.Max(240, root.ActualWidth - 96)) };
        body.SetRelease(release);
        var dialog = new ContentDialog { Title = Loc.Get("Update_Title"), Content = body, XamlRoot = root.XamlRoot };
        body.CancelRequested += (_, _) => dialog.Hide();
#if FILESMATE_UI_TEST
        _updateDemoDialog = dialog;
        if (Environment.GetEnvironmentVariable("FILESMATE_UPDATE_DEMO") == "1") body.ShowDemoLabel();
#endif
        ContentDialogTheme.Apply(dialog, root);
        dialog.Closing += (_, _) => cancellation.Cancel();
        string? installer = null;
        try
        {
            var visible = dialog.ShowAsync();
            try
            {
                installer = await App.Updates.DownloadAsync(release, new Progress<double>(value =>
                { body.SetProgress(value); }), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                body.SetStatus(Loc.Get("Update_Verifying"));
                await App.Updates.InstallAsync(release, installer, cancellation.Token);
                dialog.Hide();
            }
            catch (OperationCanceledException)
            {
                if (!cancellation.IsCancellationRequested) body.SetStatus(Loc.Get("Update_Timeout"), failed: true);
                else dialog.Hide();
            }
            catch (Exception error)
            {
                App.LogFailure("UpdateInstall", error);
                body.SetStatus(error is InvalidDataException ? Loc.Get("Update_VerificationFailed")
                    : error is InvalidOperationException ? error.Message : Loc.Get("Update_Failed"), failed: true);
            }
            await visible;
        }
        catch (Exception error) { App.LogFailure("UpdateDialog", error); }
        finally
        {
            _updateDialogOpen = false;
            if (!App.Updates.IsInstalling && !_windowClosed) ShowUpdateNotice(release);
            if (installer is not null && !App.Updates.IsInstalling)
            {
                try { File.Delete(installer); } catch (IOException) { }
            }
        }
    }
}
