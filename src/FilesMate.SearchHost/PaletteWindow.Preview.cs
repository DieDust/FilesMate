using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Icons;
using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private ICSharpCode.AvalonEdit.TextEditor? _previewText;
    private ICSharpCode.AvalonEdit.TextEditor PreviewText
    {
        get
        {
            if (_previewText is not null) return _previewText;
            var editor = new ICSharpCode.AvalonEdit.TextEditor
            {
                IsReadOnly = true, ShowLineNumbers = true,
                WordWrap = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
                Padding = new Thickness(0), Visibility = Visibility.Collapsed,
            };
            editor.Document.UndoStack.SizeLimit = 0;
            editor.Resources[typeof(ScrollViewer)] = (Style)FindResource("PreviewScrollViewer");
            editor.Loaded += (_, _) =>
            {
                if (FindChild<ScrollViewer>(editor) is { } scroll)
                    scroll.Style = (Style)FindResource("PreviewScrollViewer");
            };
            editor.SetResourceReference(Control.ForegroundProperty, "Ink");
            editor.SetResourceReference(ICSharpCode.AvalonEdit.TextEditor.LineNumbersForegroundProperty, "Muted");
            System.Windows.Automation.AutomationProperties.SetAutomationId(editor, "SearchPreviewText");
            PreviewTextHost.Content = _previewText = editor;
            return editor;
        }
    }

    private void ClearTextPreview()
    {
        if (_previewText is null) return;
        _previewText.Text = "";
        _previewText.SyntaxHighlighting = null;
        _previewText.Visibility = Visibility.Collapsed;
    }

    private void ReleaseTextPreview()
    {
        ClearTextPreview();
        PreviewTextHost.Content = null;
        _previewText = null;
    }

    private readonly TextPreviewProvider _textPreview = new();
    private readonly PdfPreviewProvider _pdfPreview = new();
    private readonly OfficePreviewProvider _officePreview = new();
    private bool _suppressPreviewSelection;
    private CancellationTokenSource? _previewQuery;
    private SearchRow? _previewRow;
    private readonly DispatcherTimer _previewFocusTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    internal string LastFocusDismiss { get; private set; } = "";

    private void CheckPreviewFocus()
    {
        if (_opening || _launching || _dragging || _contextOpen || _previewResize is not null || !IsVisible
            || RankingPanel.IsVisible && Environment.TickCount64 < _rankFocusGraceUntil) return;
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;
        if (foreground == new WindowInteropHelper(this).Handle) return;
        if (_nativeOffice?.OwnsWindow(foreground) == true) return;
        if (PreviewPopup.IsOpen && PresentationSource.FromVisual(PreviewCard) is HwndSource popup && foreground == popup.Handle) return;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        string name;
        try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { name = "closed"; }
        LastFocusDismiss = $"foreground={foreground:X}/{pid}/{name}; main={new WindowInteropHelper(this).Handle:X}; popup={(PresentationSource.FromVisual(PreviewCard) as HwndSource)?.Handle:X}";
        Dismiss();
    }

    private void Preview_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pending || _contextOpen || _suppressPreviewSelection || System.Windows.Input.Mouse.RightButton == System.Windows.Input.MouseButtonState.Pressed) return;
        QueuePreview(e.AddedItems.OfType<SearchRow>().LastOrDefault() ?? Results.SelectedItem as SearchRow);
    }
    internal void ApplyPreviewSetting()
    {
        if (!_host.Settings.PreviewEnabled) ClearPreview();
    }
    private void Preview_Close(object sender, RoutedEventArgs e) => ClearPreview();

    private void ApplyPreviewRoundRegion()
    {
        // An opaque popup can host native WebView2; clip its HWND to the same
        // silhouette as the themed card instead of using a layered window.
        if (!PreviewPopup.IsOpen || PresentationSource.FromVisual(PreviewCard) is not HwndSource popup ||
            !Native.GetWindowRect(popup.Handle, out var bounds)) return;
        var dpi = VisualTreeHelper.GetDpi(PreviewCard);
        var diameter = (int)Math.Round(PreviewCard.CornerRadius.TopLeft * 2 * dpi.DpiScaleX);
        var region = Native.CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1, diameter, diameter);
        if (region != IntPtr.Zero && Native.SetWindowRgn(popup.Handle, region, true) == 0) Native.DeleteObject(region);
    }

    private bool PositionPreview()
    {
        var scale = VisualTreeHelper.GetDpi(this);
        var main = PointToScreen(new Point());
        var target = ResultBody.PointToScreen(new Point());
        var right = main.X + ActualWidth * scale.DpiScaleX;
        var bottom = main.Y + ActualHeight * scale.DpiScaleY;
        var rightRoom = (_workArea.Right - right) / scale.DpiScaleX - 16;
        var leftRoom = (main.X - _workArea.Left) / scale.DpiScaleX - 16;
        PreviewPopup.PlacementTarget = this;
        PreviewPopup.VerticalOffset = (target.Y - main.Y) / scale.DpiScaleY;
        var preferredWidth = _previewSize?.Width ?? 400;
        var preferredHeight = _previewSize?.Height ?? Math.Max(140, Results.MaxHeight - 4);
        PreviewCard.Height = Math.Min(preferredHeight, Math.Max(140, (_workArea.Bottom - target.Y) / scale.DpiScaleY - 12));
        if (rightRoom >= 180)
        {
            PreviewCard.Width = Math.Min(preferredWidth, rightRoom);
            PreviewPopup.Placement = PlacementMode.Right;
            PreviewPopup.HorizontalOffset = 16;
        }
        else if (leftRoom >= 180)
        {
            PreviewCard.Width = Math.Min(preferredWidth, leftRoom);
            PreviewPopup.Placement = PlacementMode.Left;
            PreviewPopup.HorizontalOffset = -16;
        }
        else
        {
            PreviewCard.Width = Math.Min(preferredWidth, ActualWidth);
            var below = (_workArea.Bottom - bottom) / scale.DpiScaleY - 12;
            var above = (main.Y - _workArea.Top) / scale.DpiScaleY - 12;
            if (Math.Max(below, above) < 140) return false;
            var useBelow = below >= above;
            PreviewCard.Height = Math.Min(PreviewCard.Height, useBelow ? below : above);
            PreviewPopup.HorizontalOffset = 0;
            PreviewPopup.Placement = useBelow ? PlacementMode.Bottom : PlacementMode.Top;
            PreviewPopup.VerticalOffset = useBelow ? 12 : -12;
        }
        return true;
    }

    /// <summary>
    /// Cancels and disposes a preview query. Cancellation runs registered callbacks inline, and those can
    /// re-enter <see cref="ClearPreview"/> or <see cref="QueuePreview"/>; callers therefore detach the field
    /// before retiring, and a query retired twice must not bring the host down.
    /// </summary>
    private static void RetireQuery(CancellationTokenSource? query)
    {
        if (query is null) return;
        try { query.Cancel(); }
        catch (ObjectDisposedException) { }
        query.Dispose();
    }

    private void CancelPreviewRead()
    {
        var query = _previewQuery;
        _previewQuery = null;
        _previewRow = null;
        RetireQuery(query);
        ReleaseDocumentPreview();
        ClearTextPreview();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewMessage.Visibility = Visibility.Visible;
        PreviewMessage.Text = Loc.Get("Search_Looking");
        PreviewTitle.Text = Loc.Get("PreviewContentTab");
        PreviewNote.Text = "";
    }

    private void ClearPreview()
    {
        CancelPreviewRead();
        PreviewCard.Visibility = Visibility.Collapsed;
        PreviewPopup.IsOpen = false;
    }

    private async void QueuePreview(SearchRow? row, int delay = 140)
    {
        if (_contextOpen || _suppressPreviewSelection || !_host.Settings.PreviewEnabled || row is null || row.IsApplication || row.Hit.IsDirectory ||
            (!_textPreview.CanHandle(row.Path) && !_pdfPreview.CanHandle(row.Path) && !_officePreview.CanHandle(row.Path) && !FileTypeIconCatalog.IsThumbnailPath(row.Path)))
        { ClearPreview(); return; }
        if (_previewRow == row && _previewQuery is not null) return;
        var previous = _previewQuery;
        var request = _previewQuery = new CancellationTokenSource();
        var token = request.Token;
        _previewRow = row;
        RetireQuery(previous);
        try
        {
            await Task.Delay(delay, token);
            if (!IsVisible || !SearchPanel.IsVisible || _contextOpen) return;
            ReleaseDocumentPreview();
            PreviewTitle.Text = row.DisplayName;
            PreviewTitle.ToolTip = row.Path;
            ClearTextPreview();
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            PreviewMessage.Text = Loc.Get("Preview_Loading");
            PreviewMessage.Visibility = Visibility.Visible;
            PreviewNote.Text = "";
            PreviewCard.Visibility = Visibility.Visible;
            if (!PositionPreview()) { ClearPreview(); return; }
            PreviewPopup.IsOpen = true;
            if (_pdfPreview.CanHandle(row.Path))
            {
                await ShowDocumentPreviewAsync(row.Path, null, token);
            }
            else if (_officePreview.CanHandle(row.Path))
            {
                if (!await TryNativeOfficeAsync(row.Path, token)) await ShowLightweightOfficeAsync(row.Path, token);
            }
            else if (_textPreview.CanHandle(row.Path))
            {
                var result = await Task.Run(() => _textPreview.CreateAsync(new PreviewRequest(row.Path, 0, 256 * 1024), token), token);
                token.ThrowIfCancellationRequested();
                if (result is PreviewResult.Text text)
                {
                    if (MarkdownPreview.CanHandle(row.Path))
                    {
                        await ShowDocumentPreviewAsync(row.Path, text.Content, token);
                        token.ThrowIfCancellationRequested();
                        PreviewNote.Text = text.IsTruncated ? Loc.Get("Preview_TextLimit") : "";
                        return;
                    }
                    PreviewText.Text = text.Content;
                    PreviewText.SyntaxHighlighting = PreviewHighlighting.ForPath(row.Path, PaletteAppearance.IsDark(_appearance));
                    foreach (var margin in PreviewText.TextArea.LeftMargins.OfType<FrameworkElement>())
                        if (margin.GetType().Name == "LineNumberMargin") margin.Margin = new Thickness(0, 0, 8, 0);
                    PreviewText.ScrollToHome();
                    PreviewText.Visibility = Visibility.Visible;
                    PreviewMessage.Visibility = text.Content.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                    PreviewMessage.Text = Loc.Get("Preview_EmptyText");
                    PreviewNote.Text = text.IsTruncated ? Loc.Get("Preview_TextLimit") : "";
                }
                else PreviewMessage.Text = Loc.Get("Preview_TextUnsupported");
            }
            else
            {
                var image = await _icons.Media.LoadAsync(row.Path, 512, token);
                token.ThrowIfCancellationRequested();
                PreviewImage.Source = image;
                PreviewImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
                PreviewMessage.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
                PreviewMessage.Text = Loc.Get("Preview_None");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (_previewQuery == request) PreviewMessage.Text = Loc.Get("Preview_ReadFailed");
        }
    }
}
