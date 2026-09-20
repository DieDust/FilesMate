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
    private UpdateNoticeCard? _updateNotice;
    private UpdateRelease? _noticeRelease;
    private bool _updateDialogOpen;
#if FILESMATE_UI_TEST
    private ContentDialog? _updateDemoDialog;
#endif
    internal bool CanInstallUpdate => !_windowClosed && Tabs.TabItems.OfType<TabViewItem>()
        .All(tab => tab.Tag is not NavigatorTabContent state || state.Navigator?.CanInstallUpdate != false);

    internal void ShowUpdateNotice(UpdateRelease release)
    {
        if (_windowClosed || Content is not Panel root) return;
        if (_updateNotice is null)
        {
            _updateNotice = new UpdateNoticeCard { HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 72), Width = 380 };
            _updateNotice.DismissRequested += (_, _) => _updateNotice.Visibility = Visibility.Collapsed;
            _updateNotice.UpdateRequested += async (_, _) =>
            {
                if (_noticeRelease is { } available) await DownloadAndInstallUpdateAsync(available);
            };
            root.SizeChanged += (_, _) => PositionUpdateNotice();
            _updateNotice.Loaded += (_, _) => PositionUpdateNotice();
            Grid.SetRowSpan(_updateNotice, 2);
            Canvas.SetZIndex(_updateNotice, 20);
            root.Children.Add(_updateNotice);
        }
        _noticeRelease = release;
        _updateNotice.SetVersion(release.DisplayVersion);
        _updateNotice.Visibility = Visibility.Visible;
        PositionUpdateNotice();
    }

    private void PositionUpdateNotice()
    {
        if (_updateNotice is null || Content is not FrameworkElement root) return;
        var left = 16d;
        var bottom = 72d;
        if (FindDescendant<Button>(root, b => b.Name == "SettingsButton" && b.IsLoaded && b.ActualHeight > 0) is { } settings)
        {
            var point = settings.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
            left = Math.Max(12, point.X);
            bottom = Math.Max(16, root.ActualHeight - point.Y + 8);
        }
        _updateNotice.Width = Math.Min(380, Math.Max(240, root.ActualWidth - left - 16));
        _updateNotice.Margin = new Thickness(left, 0, 16, bottom);
    }

    internal async Task DownloadAndInstallUpdateAsync(UpdateRelease release)
    {
        if (_windowClosed || _updateDialogOpen || App.Updates.IsInstalling || Content is not FrameworkElement root) return;
        if (_updateNotice is not null) _updateNotice.Visibility = Visibility.Collapsed;
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
