using Loc = FilesMate.App.Localization.StringTable;
using System.IO;

using FilesMate.App.Icons;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Preview;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Media.Core;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Media.Playback;

namespace FilesMate.App.Controls.Preview;

public sealed partial class PreviewPane : UserControl
{
    private MediaPlayer? _mediaPlayer;
    private MediaSource? _mediaSource;
    private string? _mediaPath;

    private PreviewService? _service;
    private CancellationTokenSource? _loadCts;
    private WebView2? _pdfContent;
    private string? _imagePath;
    private long _generation;
    private bool _hasContent;
    private bool _showInformation;
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _visibleTextLines = new();
    private string[] _textLines = [];
    private ScrollViewer? _textScroll;

    private void AppendTextLines()
    {
        var end = Math.Min(_textLines.Length, _visibleTextLines.Count + 256);
        while (_visibleTextLines.Count < end) _visibleTextLines.Add(_textLines[_visibleTextLines.Count]);
    }
    private void TextContent_Loaded(object sender, RoutedEventArgs e)
    {
        static ScrollViewer? Find(DependencyObject node)
        {
            if (node is ScrollViewer scroll) return scroll;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                if (Find(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i)) is { } child) return child;
            return null;
        }
        if (_textScroll is not null) _textScroll.ViewChanged -= TextScroll_ViewChanged;
        _textScroll = Find(TextContent);
        if (_textScroll is not null) _textScroll.ViewChanged += TextScroll_ViewChanged;
    }
    private void TextScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_textScroll is { } scroll && scroll.ScrollableHeight - scroll.VerticalOffset < Math.Max(160, scroll.ViewportHeight)) AppendTextLines();
    }

    [System.Diagnostics.Conditional("FILESMATE_UI_TEST")]
    private void TracePreview(string message)
    {
#if FILESMATE_UI_TEST
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "preview-test.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}; host={ContentHost.ActualWidth}x{ContentHost.ActualHeight}; visible={ContentHost.Visibility}\n");
#endif
    }

    public PreviewPane()
    {
        InitializeComponent();
        ContentTab.Content = StringTable.Get("PreviewContentTab");
        InfoTab.Content = StringTable.Get("PreviewInfoTab");
        CompatiblePreviewButton.Content = StringTable.Get("PreviewCompatible");
        MediaPlayLabel.Text = StringTable.Get("PreviewPlay");
        TextContent.ItemsSource = _visibleTextLines;
        EmptyText.Text = StringTable.Get("PreviewEmpty");
        LocationLabel.Text = StringTable.Get("Preview_Location");
        ModifiedLabel.Text = StringTable.Get("Column_Modified");
        CreatedLabel.Text = StringTable.Get("Preview_Created");
        SizeLabel.Text = StringTable.Get("Column_Size");
        ItemsLabel.Text = StringTable.Get("Preview_Items");
        AttributesLabel.Text = StringTable.Get("Preview_Attributes");
        Unloaded += (_, _) => CancelAndClear();
    }

    public bool IsPreviewVisible { get; private set; }
    public event EventHandler? CloseRequested;

    public void UseAsCardContent()
    {
        Surface.SurfaceKind = Glass.GlassSurfaceKind.Chrome;
        Surface.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Surface.BorderThickness = new Thickness(0);
        Surface.Padding = new Thickness(0);
        Surface.Shadow = null;
    }

    public void Attach(PreviewService service) => _service = service ?? throw new ArgumentNullException(nameof(service));

    public void SetVisible(bool visible)
    {
        IsPreviewVisible = visible;
        if (!visible)
        {
            CancelAndClear();
        }
    }

    public async Task LoadAsync(string path, long generation, CancellationToken cancellationToken = default)
    {
        if (!IsPreviewVisible || _service is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _generation = generation;
        TracePreview("Load " + path);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCts.Token;
        _hasContent = false;
        ShowLoading();
        try
        {
            if (NativeOfficePreview.CanHandle(path))
            {
                if (!await TryNativeOfficeAsync(path, generation, token)) await ShowCompatibleOfficeAsync(path, generation, token);
                return;
            }
            var result = await _service.LoadAsync(new PreviewRequest(path, generation), token);
            if (generation != _generation || token.IsCancellationRequested)
            {
                return;
            }

            await ShowResultAsync(result, generation, token);
            TracePreview("Result " + result.Kind);
            if (token.IsCancellationRequested) return;
            _hasContent = _pdfContent is not null || TextHost.Visibility == Visibility.Visible || ImageContent.Visibility == Visibility.Visible || MediaPlayButton.Visibility == Visibility.Visible || MediaContent.Visibility == Visibility.Visible || DetailsText.Visibility == Visibility.Visible;
            ApplyPaneMode();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ShowDetails(error.Message);
        }
    }

    public void CancelAndClear()
    {
        ReleaseNativeOffice();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _imagePath = null;
        ReleasePdfPreview();
        ReleaseMediaPreview();
        ImageContent.Source = null;
        _visibleTextLines.Clear();
        _textLines = [];
        DetailsText.Text = string.Empty;
        ItemName.Text = string.Empty;
        ItemType.Text = string.Empty;
        ShellIconBinder.Clear(ItemIcon, ItemGlyph);
        EmptyText.Visibility = Visibility.Visible;
        PaneScroll.Visibility = Visibility.Collapsed;
        PaneTabs.Visibility = Visibility.Collapsed;
        ContentHost.Visibility = Visibility.Visible;
        ItemHeader.Visibility = Visibility.Collapsed;
        PropertyList.Visibility = Visibility.Collapsed;
        LoadingRing.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        TextHost.Visibility = Visibility.Collapsed;
        ImageContent.Visibility = Visibility.Collapsed;
        MediaContent.Visibility = Visibility.Collapsed;
        DetailsText.Visibility = Visibility.Collapsed;
    }

    private void ShowLoading()
    {
        ReleaseNativeOffice();
        _textLines = [];
        _visibleTextLines.Clear();
        ReleasePdfPreview();
        EmptyText.Visibility = Visibility.Collapsed;
        PaneScroll.Visibility = Visibility.Collapsed;
        ContentHost.Visibility = Visibility.Visible;
        PaneTabs.Visibility = Visibility.Collapsed;
        ReleaseMediaPreview();
        ImageContent.Source = null;
        _imagePath = null;
        ItemHeader.Visibility = Visibility.Collapsed;
        PropertyList.Visibility = Visibility.Collapsed;
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        TextHost.Visibility = Visibility.Collapsed;
        ImageContent.Visibility = Visibility.Collapsed;
        MediaContent.Visibility = Visibility.Collapsed;
        DetailsText.Visibility = Visibility.Collapsed;
    }

    private async Task ShowResultAsync(
        PreviewResult result,
        long generation,
        CancellationToken cancellationToken)
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        PaneScroll.Visibility = Visibility.Visible;
        TextHost.Visibility = Visibility.Collapsed;
        ImageContent.Visibility = Visibility.Collapsed;
        MediaContent.Visibility = Visibility.Collapsed;
        DetailsText.Visibility = Visibility.Collapsed;
        switch (result)
        {
            case PreviewResult.Text text:
                if (MarkdownPreview.CanHandle(text.Path))
                {
                    await ShowPdfAsync(text.Path, generation, cancellationToken, text.Content);
                    cancellationToken.ThrowIfCancellationRequested();
                    // Keep rendered Markdown visible; truncation must not replace the browser.
                    BindFromPath(text.Path);
                    break;
                }
                _textLines = (text.IsTruncated
                    ? text.Content + Environment.NewLine + Environment.NewLine + StringTable.Get("PreviewTruncated")
                    : text.Content).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                AppendTextLines();
                _textScroll?.ChangeView(0, 0, null, true);
                TextHost.Visibility = Visibility.Visible;
                BindFromPath(text.Path);
                break;
            case PreviewResult.Image image:
                await ShowImageAsync(image.Path, generation, cancellationToken);
                if (ImageContent.Visibility == Visibility.Visible)
                {
                    BindFromPath(image.Path);
                }

                break;
            case PreviewResult.Media media:
                await ShowMediaAsync(media.Path, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                BindFromPath(media.Path);
                break;
            case PreviewResult.Properties properties:
                BindProperties(properties);
                break;
            case PreviewResult.Html html:
                await ShowPdfAsync(html.Path, generation, cancellationToken, html: html.Content);
                cancellationToken.ThrowIfCancellationRequested();
                BindFromPath(html.Path);
                break;
            case PreviewResult.Pdf:
                await ShowPdfAsync(result.Path, generation, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                BindFromPath(result.Path);
                break;
            case PreviewResult.Unsupported unsupported:
                BindFromPath(unsupported.Path);
                ShowDetails(unsupported.Reason);
                break;
        }
    }

    private async Task ShowImageAsync(string path, long generation, CancellationToken cancellationToken)
    {
        _imagePath = path;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = await file.OpenReadAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            var size = PreviewImageSize.Fit(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight,
                ContentHost.ActualWidth, ContentHost.ActualHeight, XamlRoot?.RasterizationScale ?? 1);
            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = size.Width, DecodePixelHeight = size.Height };
            await bitmap.SetSourceAsync(stream);
            if (generation != _generation || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ImageContent.Source = bitmap;
            ImageContent.Visibility = Visibility.Visible;
            TracePreview($"Image decode {size.Width}x{size.Height}");
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
        }

        var thumbnail = await ShellIconBinder.GetThumbnailAsync(path, 512, cancellationToken);
        if (generation != _generation || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (thumbnail is not null)
        {
            ImageContent.Source = ShellIconBinder.ToBitmap(thumbnail);
            ImageContent.Visibility = Visibility.Visible;
            return;
        }

        ShowFileProperties(path);
    }

    private async Task ShowMediaAsync(string path, CancellationToken cancellationToken)
    {
        _mediaPath = path;
        TracePreview("Media poster; player=none");
        MediaPlayButton.Visibility = Visibility.Visible;
        MediaPlayButton.IsEnabled = true;
        var thumbnail = await ShellIconBinder.GetThumbnailAsync(path, 512, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_mediaPath != path || _mediaPlayer is not null) return;
        if (thumbnail is not null)
        {
            ImageContent.Source = ShellIconBinder.ToBitmap(thumbnail);
            ImageContent.Visibility = Visibility.Visible;
        }
    }

    private void ReleaseMediaPreview()
    {
        _mediaPath = null;
        MediaPlayButton.Visibility = Visibility.Collapsed;
        _mediaPlayer?.Pause();
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.MediaFailed -= MediaPlayer_Failed;
            _mediaPlayer.MediaOpened -= MediaPlayer_Opened;
        }
        MediaContent.Source = null;
        MediaContent.SetMediaPlayer(null);
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
        _mediaSource?.Dispose();
        _mediaSource = null;
        TracePreview("Media released; player=none");
    }

    private async void MediaPlayButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _mediaPath;
        var request = _loadCts;
        if (path is null || request is null || request.IsCancellationRequested) return;
        MediaPlayButton.IsEnabled = false;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            if (_loadCts != request || request.IsCancellationRequested || _mediaPath != path) return;
            _mediaSource = MediaSource.CreateFromStorageFile(file);
            _mediaPlayer = new MediaPlayer { AutoPlay = false };
            _mediaPlayer.MediaFailed += MediaPlayer_Failed;
            _mediaPlayer.MediaOpened += MediaPlayer_Opened;
            MediaContent.SetMediaPlayer(_mediaPlayer);
            MediaContent.Source = _mediaSource;
            MediaContent.Visibility = Visibility.Visible;
            ImageContent.Source = null;
            ImageContent.Visibility = Visibility.Collapsed;
            MediaPlayButton.Visibility = Visibility.Collapsed;
            _mediaPlayer.Play();
            TracePreview("Media play; player=created");
        }
        catch (Exception error)
        {
            if (_loadCts != request || request.IsCancellationRequested) return;
            ReleaseMediaPreview();
            ShowDetails(StringTable.Get("PreviewMediaError"));
            System.Diagnostics.Trace.TraceError("Media preview: {0}", error);
        }
    }

    private void MediaPlayer_Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(sender, _mediaPlayer)) return;
            ReleaseMediaPreview();
            MediaContent.Visibility = Visibility.Collapsed;
            ShowDetails(StringTable.Get("PreviewMediaError"));
        });
    }

    private void MediaPlayer_Opened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(sender, _mediaPlayer)) TracePreview("Media opened");
        });
    }

    private void ImageContent_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_imagePath is string path)
        {
            ShowFileProperties(path);
        }
    }

    private void BindFromPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                BindProperties(new PreviewResult.Properties(
                    path,
                    0,
                    directory.Exists ? directory.LastWriteTimeUtc : null,
                    directory.Exists ? directory.Attributes : 0,
                    IsDirectory: true,
                    ChildCount: CountChildren(directory),
                    CreationTimeUtc: directory.Exists ? directory.CreationTimeUtc : null));
                return;
            }

            var info = new FileInfo(path);
            BindProperties(new PreviewResult.Properties(
                path,
                info.Exists ? info.Length : 0,
                info.Exists ? info.LastWriteTimeUtc : null,
                info.Exists ? info.Attributes : 0,
                CreationTimeUtc: info.Exists ? info.CreationTimeUtc : null));
        }
        catch (Exception error)
        {
            BindItem(path, Directory.Exists(path));
            ShowDetails(error.Message);
        }
    }

    private void ShowFileProperties(string path) => BindFromPath(path);

    private void BindItem(string path, bool isDirectory)
    {
        ItemHeader.Visibility = Visibility.Visible;
        ItemName.Text = PreviewDetails.DisplayName(path);
        ItemType.Text = PreviewDetails.TypeLabel(path, isDirectory);
        ItemGlyph.Glyph = isDirectory ? "\uE8B7" : "\uE8A5";
        ShellIconBinder.BindPath(ItemIcon, ItemGlyph, path, isDirectory, 48);
    }

    private void BindProperties(PreviewResult.Properties properties)
    {
        var format = App.ExplorerPreferences.DateFormat;
        BindItem(properties.Path, properties.IsDirectory);
        PropertyList.Visibility = Visibility.Visible;
        LocationValue.Text = PreviewDetails.Location(properties.Path);
        ModifiedValue.Text = PreviewDetails.Modified(properties.LastWriteTimeUtc, format);
        CreatedValue.Text = PreviewDetails.Modified(properties.CreationTimeUtc, format);
        SizeRow.Visibility = properties.IsDirectory ? Visibility.Collapsed : Visibility.Visible;
        SizeValue.Text = properties.IsDirectory ? string.Empty : DriveCapacity.FormatBytes(properties.Length);
        ItemsRow.Visibility = properties.IsDirectory ? Visibility.Visible : Visibility.Collapsed;
        ItemsValue.Text = properties.IsDirectory ? PreviewDetails.Items(properties.ChildCount) : string.Empty;
        var attributes = PreviewDetails.Attributes(properties.Attributes);
        AttributesRow.Visibility = attributes is null ? Visibility.Collapsed : Visibility.Visible;
        AttributesValue.Text = attributes ?? string.Empty;
    }

    private static int? CountChildren(DirectoryInfo directory)
    {
        try
        {
            var count = 0;
            foreach (var _ in directory.EnumerateFileSystemInfos())
            {
                count++;
                if (count > 500)
                {
                    return 501;
                }
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ShowDetails(string text)
    {
        ReleasePdfPreview();
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        PaneScroll.Visibility = Visibility.Visible;
        DetailsText.Text = text;
        DetailsText.Visibility = Visibility.Visible;
        _hasContent = true;
        ApplyPaneMode();
    }

    private async Task ShowPdfAsync(string path, long generation, CancellationToken cancellationToken, string? markdown = null, string? html = null)
    {
        ReleasePdfPreview();
        var failureText = markdown is null && html is null ? StringTable.Get("PreviewPdfHint") : Loc.Get("Preview_DocumentUnavailable");
        var view = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(view, "DocumentContent");
        _pdfContent = view;
        view.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.Space)
            { e.Handled = true; DispatcherQueue.TryEnqueue(() => { if (_pdfContent == view) CloseRequested?.Invoke(this, EventArgs.Empty); }); }
        }), true);
        ContentHost.Children.Add(view);
        TracePreview("WebView added");
        view.SizeChanged += (_, _) => TracePreview($"WebView size {view.ActualWidth}x{view.ActualHeight}");
        try
        {
            var profile = Program.SettingsPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate", "document-preview"));
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null);
            await view.EnsureCoreWebView2Async(environment);
            TracePreview("WebView ready");
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _generation || !ReferenceEquals(_pdfContent, view))
            {
                return;
            }

            view.NavigationCompleted += (_, args) =>
            {
                TracePreview($"Navigation {args.IsSuccess} {args.WebErrorStatus}");
                if (!args.IsSuccess && ReferenceEquals(_pdfContent, view))
                {
                    ShowDetails(failureText);
                }
            };
            var core = view.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.IsWebMessageEnabled = markdown is null && html is null;
            core.WebMessageReceived += (_, e) =>
            {
                if (e.Source == PdfPreviewAssets.ViewerUri && e.TryGetWebMessageAsString() == "close-preview") DispatcherQueue.TryEnqueue(() => { if (_pdfContent == view) CloseRequested?.Invoke(this, EventArgs.Empty); });
            };
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.HiddenPdfToolbarItems = Microsoft.Web.WebView2.Core.CoreWebView2PdfToolbarItems.Save
                | Microsoft.Web.WebView2.Core.CoreWebView2PdfToolbarItems.SaveAs
                | Microsoft.Web.WebView2.Core.CoreWebView2PdfToolbarItems.Print
                | Microsoft.Web.WebView2.Core.CoreWebView2PdfToolbarItems.FullScreen;
            core.PermissionRequested += (_, args) => args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.ProcessFailed += (_, _) =>
            {
                if (ReferenceEquals(_pdfContent, view)) ShowDetails(failureText);
            };
            core.Profile.PreferredColorScheme = ActualTheme == ElementTheme.Dark
                ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
            if (html is not null)
            {
                ConfigureOfficeDocument(core, html);
            }
            else if (markdown is null)
            {
                using (PdfPreviewAssets.OpenDocument(path)) { }
                core.Settings.IsScriptEnabled = true;
                core.Settings.IsWebMessageEnabled = true;
                var documentUri = PdfPreviewAssets.ViewerUri;
                core.NavigationStarting += (_, args) =>
                {
                    args.Cancel = args.Uri.Split('#')[0] != documentUri;
                };
                core.SetVirtualHostNameToFolderMapping(PdfPreviewAssets.Host, PdfPreviewAssets.Folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
                core.AddWebResourceRequestedFilter(PdfPreviewAssets.DocumentUri, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    try
                    {
                        var response = PdfPreviewAssets.OpenResponse(path, e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null);
                        e.Response = core.Environment.CreateWebResourceResponse(response.Body.AsRandomAccessStream(), response.Status, response.Status == 206 ? "Partial Content" : "OK", response.Headers);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", ""); }
                };
                core.Navigate(documentUri);
            }
            else
            {
                core.NavigationStarting += (_, args) =>
                {
                    args.Cancel = !MarkdownPreview.IsDocumentNavigation(args.Uri, args.IsUserInitiated);
                    if (args.Cancel && args.IsUserInitiated && Uri.TryCreate(args.Uri, UriKind.Absolute, out var link)
                        && link.Host != "filesmate-preview.local" && link.Scheme is "https" or "http")
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true }); }
                        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowDetails(Loc.Get("Preview_LinkFailed")); }
                    }
                };
                core.SetVirtualHostNameToFolderMapping("filesmate-preview.local", Path.GetDirectoryName(Path.GetFullPath(path))!, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
                var dark = ActualTheme == ElementTheme.Dark;
                string Color(string key, string fallback)
                {
                    if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
                        return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
                    return fallback;
                }
                var background = Color("FilesMate.SearchPanel.BackgroundBrush", dark ? "#282828" : "#F9F9F9");
                var foreground = Color("FilesMate.Text.PrimaryBrush", dark ? "#F2F2F2" : "#202020");
                var accent = Color("FilesMate.AccentBrush", dark ? "#60CDFF" : "#0067C0");
                var rendered = await Task.Run(() => MarkdownPreview.Render(markdown, dark, background, foreground, accent), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (generation != _generation || !ReferenceEquals(_pdfContent, view)) return;
                core.NavigateToString(rendered);
            }
            view.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_pdfContent, view))
            {
                ShowDetails(error is IOException or UnauthorizedAccessException ? error.Message : failureText);
            }
        }
    }

    private void ContentTab_Click(object sender, RoutedEventArgs e) { _showInformation = false; ApplyPaneMode(); }
    private void InfoTab_Click(object sender, RoutedEventArgs e) { _showInformation = true; ApplyPaneMode(); }

    private void ApplyPaneMode()
    {
        PaneTabs.Visibility = _hasContent ? Visibility.Visible : Visibility.Collapsed;
        var information = !_hasContent || _showInformation;
        if (information) _mediaPlayer?.Pause();
        PaneScroll.Visibility = information ? Visibility.Visible : Visibility.Collapsed;
        ContentHost.Visibility = information ? Visibility.Collapsed : Visibility.Visible;
        UpdateNativeOfficeBounds();
        var selected = Application.Current.Resources["FilesMate.Item.SelectedBrush"] as Microsoft.UI.Xaml.Media.Brush;
        var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ContentTab.Background = information ? transparent : selected;
        InfoTab.Background = information ? selected : transparent;
    }

    private static void ConfigureOfficeDocument(Microsoft.Web.WebView2.Core.CoreWebView2 core, string html)
    {
        const string uri = "https://filesmate-office.local/preview.html";
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri.Split('#')[0] != uri;
        core.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var allowed = e.Request.Uri.Split('#')[0] == uri;
            var bytes = System.Text.Encoding.UTF8.GetBytes(allowed ? html : "");
            e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(bytes).AsRandomAccessStream(), allowed ? 200 : 403, allowed ? "OK" : "Forbidden", "Content-Type: text/html; charset=utf-8");
        };
        core.Navigate(uri);
    }

    private void ReleasePdfPreview()
    {
        var view = _pdfContent;
        _pdfContent = null;
        if (view is null)
        {
            return;
        }

        ContentHost.Children.Remove(view);
        view.Close();
    }
}
