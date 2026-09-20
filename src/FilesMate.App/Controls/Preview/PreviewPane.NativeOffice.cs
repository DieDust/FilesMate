using FilesMate.App.Preview;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Controls.Preview;

public sealed partial class PreviewPane
{
    private NativeOfficePreview? _nativeOffice;
    private Func<Task>? _nativeFallback;
    private Func<Task>? _nativeRetry;
    internal void SetNativePreviewOpacity(byte opacity) => _nativeOffice?.SetOpacity(opacity);

    private async Task<bool> TryNativeOfficeAsync(string path, long generation, CancellationToken token)
    {
        if (!NativeOfficePreview.CanHandle(path)) return false;
        if (!IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler onLoaded = (_, _) => loaded.TrySetResult();
            Loaded += onLoaded;
            try { await loaded.Task.WaitAsync(TimeSpan.FromSeconds(3), token); }
            catch (TimeoutException) { return false; }
            finally { Loaded -= onLoaded; }
        }
        if (XamlRoot is null) return false;
        var parent = Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        TracePreview("Native Office parent=" + parent);
        var session = await NativeOfficePreview.TryOpenAsync(path, parent, token);
        if (token.IsCancellationRequested) { session?.Dispose(); token.ThrowIfCancellationRequested(); }
        if (session is null) return false;
        if (generation != _generation) { session.Dispose(); return false; }
        _nativeOffice = session;
        session.CloseRequested += () => DispatcherQueue.TryEnqueue(() => { if (_nativeOffice == session) CloseRequested?.Invoke(this, EventArgs.Empty); });
        _nativeFallback = async () =>
        {
            if (_nativeOffice != session || token.IsCancellationRequested) return;
            await ShowCompatibleOfficeAsync(path, generation, token);
        };
        session.Failed += () => DispatcherQueue.TryEnqueue(async () =>
        { if (_nativeOffice == session && _nativeFallback is { } fallback) await fallback(); });
        if (!session.IsAlive) { ReleaseNativeOffice(); return false; }
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        BindFromPath(path);
        CompatiblePreviewButton.Visibility = Visibility.Visible;
        CompatiblePreviewButton.Content = Localization.StringTable.Get("PreviewCompatible");
        _hasContent = true;
        ApplyPaneMode();
        ContentHost.LayoutUpdated += NativeOffice_LayoutUpdated;
        UpdateNativeOfficeBounds();
        return true;
    }

    private void NativeOffice_LayoutUpdated(object? sender, object e) => UpdateNativeOfficeBounds();
    private async void CompatiblePreview_Click(object sender, RoutedEventArgs e)
    {
        var action = _nativeFallback ?? _nativeRetry;
        if (action is null || !CompatiblePreviewButton.IsEnabled) return;
        CompatiblePreviewButton.IsEnabled = false;
        try { _showInformation = false; await action(); }
        catch (OperationCanceledException) { }
        finally { CompatiblePreviewButton.IsEnabled = true; }
    }

    private async Task ShowCompatibleOfficeAsync(string path, long generation, CancellationToken token)
    {
        ShowLoading();
        try
        {
            var result = await _service!.LoadAsync(new PreviewRequest(path, generation), token);
            if (token.IsCancellationRequested || generation != _generation) return;
            await ShowResultAsync(result, generation, token);
            if (token.IsCancellationRequested) return;
            _hasContent = true;
            ApplyPaneMode();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error) { if (!token.IsCancellationRequested) ShowDetails(error.Message); }
        if (token.IsCancellationRequested || generation != _generation) return;
        _nativeRetry = async () =>
        {
            if (token.IsCancellationRequested || generation != _generation) return;
            ShowLoading();
            if (!await TryNativeOfficeAsync(path, generation, token))
                await ShowCompatibleOfficeAsync(path, generation, token);
        };
        CompatiblePreviewButton.Content = Localization.StringTable.Get("PreviewNative");
        CompatiblePreviewButton.Visibility = Visibility.Visible;
    }
    private void UpdateNativeOfficeBounds()
    {
        if (_nativeOffice is not { } session || XamlRoot is null) return;
        var point = ContentHost.TransformToVisual(XamlRoot.Content).TransformPoint(default);
        var scale = XamlRoot.RasterizationScale;
        session.SetBounds((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale),
            (int)Math.Round(ContentHost.ActualWidth * scale), (int)Math.Round(ContentHost.ActualHeight * scale),
            IsPreviewVisible && ContentHost.Visibility == Visibility.Visible && IsLoaded);
    }
    private void ReleaseNativeOffice()
    {
        ContentHost.LayoutUpdated -= NativeOffice_LayoutUpdated;
        CompatiblePreviewButton.Visibility = Visibility.Collapsed;
        _nativeFallback = null;
        _nativeRetry = null;
        var session = _nativeOffice;
        _nativeOffice = null;
        session?.Dispose();
    }
}
