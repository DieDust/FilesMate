using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Preview;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private NativeOfficePreview? _nativeOffice;
    private Func<Task>? _nativeFallback;
    private Func<Task>? _nativeRetry;
    private async Task<bool> TryNativeOfficeAsync(string path, CancellationToken token)
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded, token);
        PreviewCard.UpdateLayout();
        if (PresentationSource.FromVisual(PreviewContentHost) is not HwndSource source) return false;
        var session = await NativeOfficePreview.TryOpenAsync(path, source.Handle, token);
        if (token.IsCancellationRequested) { session?.Dispose(); token.ThrowIfCancellationRequested(); }
        if (session is null) return false;
        _nativeOffice = session;
        session.CloseRequested += () => Dispatcher.BeginInvoke(new Action(() => { if (_nativeOffice == session) ClearPreview(); }));
        _nativeFallback = async () =>
        {
            if (_nativeOffice != session || token.IsCancellationRequested) return;
            ReleaseNativeOffice();
            try { await ShowLightweightOfficeAsync(path, token); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (token.IsCancellationRequested) return;
                System.Diagnostics.Trace.TraceError(error.ToString());
                DocumentPreviewStatus = "Office fallback failed: " + error.Message;
                PreviewMessage.Visibility = Visibility.Visible;
                PreviewMessage.Text = Loc.Get("Preview_DocumentUnavailable");
            }
        };
        session.Failed += () => Dispatcher.BeginInvoke(new Action(async () =>
        { if (_nativeOffice == session && _nativeFallback is { } fallback) await fallback(); }));
        if (!session.IsAlive) { ReleaseNativeOffice(); return false; }
        PreviewMessage.Visibility = Visibility.Collapsed;
        DocumentPreviewStatus = "native-office";
        CompatiblePreviewButton.Visibility = Visibility.Visible;
        CompatiblePreviewButton.Content = Loc.Get("PreviewCompatible");
        PreviewContentHost.LayoutUpdated += NativeOffice_LayoutUpdated;
        UpdateNativeOfficeBounds();
        return true;
    }
    private async Task ShowLightweightOfficeAsync(string path, CancellationToken token)
    {
        PreviewMessage.Visibility = Visibility.Visible;
        PreviewMessage.Text = Loc.Get("Preview_LoadingCompatible");
        var result = await _officePreview.CreateAsync(new PreviewRequest(path, 0), token);
        token.ThrowIfCancellationRequested();
        if (result is PreviewResult.Html html) await ShowDocumentPreviewAsync(path, null, token, html.Content);
        else PreviewMessage.Text = (result as PreviewResult.Unsupported)?.Reason ?? Loc.Get("Preview_DocumentFailed");
        token.ThrowIfCancellationRequested();
        _nativeRetry = async () =>
        {
            token.ThrowIfCancellationRequested();
            ReleaseDocumentPreview();
            PreviewMessage.Visibility = Visibility.Visible;
            PreviewMessage.Text = Loc.Get("Preview_LoadingNative");
            if (!await TryNativeOfficeAsync(path, token)) await ShowLightweightOfficeAsync(path, token);
        };
        CompatiblePreviewButton.Content = Loc.Get("PreviewNative");
        CompatiblePreviewButton.Visibility = Visibility.Visible;
    }
    private void NativeOffice_LayoutUpdated(object? sender, EventArgs e) => UpdateNativeOfficeBounds();
    private async void CompatiblePreview_Click(object sender, RoutedEventArgs e)
    {
        var action = _nativeFallback ?? _nativeRetry;
        if (action is null || !CompatiblePreviewButton.IsEnabled) return;
        CompatiblePreviewButton.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { }
        finally { CompatiblePreviewButton.IsEnabled = true; }
    }
    private void UpdateNativeOfficeBounds()
    {
        if (_nativeOffice is not { } session || PresentationSource.FromVisual(PreviewContentHost) is not HwndSource source || source.RootVisual is not Visual root) return;
        var point = PreviewContentHost.TransformToAncestor(root).Transform(new Point());
        var scale = VisualTreeHelper.GetDpi(PreviewContentHost);
        session.SetBounds((int)Math.Round(point.X * scale.DpiScaleX), (int)Math.Round(point.Y * scale.DpiScaleY),
            (int)Math.Round(PreviewContentHost.ActualWidth * scale.DpiScaleX), (int)Math.Round(PreviewContentHost.ActualHeight * scale.DpiScaleY), PreviewPopup.IsOpen && PreviewCard.IsVisible);
    }
    private void ReleaseNativeOffice()
    {
        PreviewContentHost.LayoutUpdated -= NativeOffice_LayoutUpdated;
        CompatiblePreviewButton.Visibility = Visibility.Collapsed;
        _nativeFallback = null;
        _nativeRetry = null;
        var session = _nativeOffice;
        _nativeOffice = null;
        session?.Dispose();
    }
}
