using System.Windows;
using System.Windows.Media;
using FilesMate.App.Models;
using FilesMate.App.Preview;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private readonly MarkdownThemeUpdates _readerThemes = new();
    private WebView2? _readerDocumentView;
    private CancellationToken _readerDocumentToken;
    private bool _readerIsPdf;
    private string? _readerDocumentStamp;

    private MarkdownReaderTheme CaptureReaderTheme()
    {
        var dark = PaletteAppearance.IsDark(_appearance);
        var palette = SkinPalette.For(dark, _appearance);
        static string Rgb(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (SystemParameters.HighContrast)
        {
            var systemBackground = Rgb(SystemColors.WindowColor);
            var foreground = Rgb(SystemColors.WindowTextColor);
            return MarkdownPreview.NormalizeTheme(new(dark, true, systemBackground, foreground, foreground,
                systemBackground, foreground, foreground, Rgb(SystemColors.HighlightColor), Rgb(SystemColors.HighlightTextColor)));
        }
        // Reading pixels remain opaque; flatten material alpha against the same
        // skin role used by the surrounding surface, without fading document ink.
        string Color(string key, uint fallback, uint? background = null)
        {
            if (TryFindResource(key) is not SolidColorBrush brush)
                return MarkdownPreview.FlattenColor(fallback, background ?? fallback);
            var c = brush.Color;
            return MarkdownPreview.FlattenColor((uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B),
                background ?? fallback, brush.Opacity);
        }
        var background = Color("Surface", palette.Floating);
        var backgroundRgb = Convert.ToUInt32(background[1..], 16);
        return MarkdownPreview.NormalizeTheme(new(dark, false, background,
            Color("Ink", palette.Text), Color("Muted", palette.Muted),
            Color("PillSurface", palette.Card), Color("Line", palette.Outline), Color("Accent", palette.Accent),
            Color("Selected", palette.Selected, backgroundRgb), Color("SelectedInk", palette.SelectedInk)));
    }

    private MarkdownReaderTheme BeginReaderDocument(WebView2 view, CancellationToken token, bool pdf = false)
    {
        _readerThemes.Reset(); _readerDocumentView = view; _readerDocumentToken = token; _readerIsPdf = pdf; _readerDocumentStamp = null;
        var theme = CaptureReaderTheme();
        _readerThemes.Request(theme);
        return theme;
    }

    private void ResetReaderDocument()
    {
        _readerDocumentView = null; _readerDocumentStamp = null; _readerThemes.Reset();
    }

    private void ReaderNavigationStarting(WebView2 view, ulong navigation)
    {
        if (_readerDocumentView != view || _readerDocumentToken.IsCancellationRequested) return;
        _readerDocumentStamp = null;
        _readerThemes.BeginNavigation(navigation);
        _readerThemes.Request(CaptureReaderTheme());
    }

    private async void ReaderNavigationCompleted(WebView2 view, ulong navigation)
    {
        if (_readerDocumentView != view || _readerDocumentToken.IsCancellationRequested) return;
        if (!_readerThemes.IsNavigation(navigation)) return;
        var document = _readerThemes.Document;
        try
        {
            var json = await view.CoreWebView2.ExecuteScriptAsync(ReaderThemeDocument.CaptureStampScript(_readerIsPdf, Guid.NewGuid().ToString("D")));
            if (_readerDocumentView != view || _readerDocumentToken.IsCancellationRequested || document != _readerThemes.Document || !_readerThemes.IsNavigation(navigation)) return;
            if (!ReaderThemeDocument.TryReadStamp(json, out var stamp)) return;
            _readerDocumentStamp = stamp;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { return; }
        _readerThemes.MarkNavigationReady(navigation);
        RefreshReaderDocumentTheme();
    }

    private async void RefreshReaderDocumentTheme()
    {
        if (_readerDocumentView is not { } view || _documentPreview != view || _readerDocumentToken.IsCancellationRequested) return;
        if (_readerDocumentStamp is null) return;
        _readerThemes.Request(CaptureReaderTheme());
        while (_readerThemes.TryTake(out var update))
        {
            var applied = false;
            try
            {
                if (_readerDocumentView != view || _documentPreview != view || _readerDocumentToken.IsCancellationRequested
                    || update.Document != _readerThemes.Document) return;
                var core = view.CoreWebView2;
                core.Profile.PreferredColorScheme = update.Theme.Dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
                applied = await core.ExecuteScriptAsync((_readerIsPdf ? PdfPreviewTheme.ThemeUpdateScript(update.Theme, _readerDocumentStamp!) : MarkdownPreview.ThemeUpdateScript(update.Theme, _readerDocumentStamp!))) == "true";
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                return;
            }
            finally { _readerThemes.Complete(update, applied); }
            if (_readerDocumentView != view) return;
        }
    }
}
