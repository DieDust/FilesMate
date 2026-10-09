using FilesMate.App.Models;
using FilesMate.App.Preview;
using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.Preview;

public sealed partial class PreviewPane
{
    private readonly MarkdownThemeUpdates _readerThemes = new();
    private WebView2? _readerView;
    private long _readerGeneration;
    private bool _readerListening;
    private bool _readerIsPdf;
    private string? _readerDocumentStamp;

    private void InitializeMarkdownThemeTracking()
    {
        Loaded += (_, _) =>
        {
            if (!_readerListening) { App.AppearanceChanged += ReaderAppearanceChanged; _readerListening = true; }
            RefreshReaderDocumentTheme();
        };
        Unloaded += (_, _) =>
        {
            App.AppearanceChanged -= ReaderAppearanceChanged;
            _readerListening = false;
            ResetReaderDocument();
        };
        ActualThemeChanged += (_, _) => RefreshReaderDocumentTheme();
    }

    private void ReaderAppearanceChanged(object? sender, AppearanceSettings settings) => RefreshReaderDocumentTheme();

    private MarkdownReaderTheme CaptureReaderTheme()
    {
        var dark = ContentDialogTheme.Resolve(this) == ElementTheme.Dark;
        var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var palette = SkinPalette.For(dark, App.AppearanceViewModel?.Effective ?? AppearanceSettings.Default);
        // WebView reading surfaces remain opaque. Flatten a material token over
        // its opaque skin role instead of discarding its alpha or fading text.
        string Color(string key, uint fallback, uint? background = null)
        {
            if (ThemeResources.Resolve(this, key) is not SolidColorBrush brush)
                return MarkdownPreview.FlattenColor(fallback, background ?? fallback);
            var c = brush.Color;
            return MarkdownPreview.FlattenColor((uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B),
                background ?? fallback, brush.Opacity);
        }
        var background = Color("FilesMate.SearchPanel.BackgroundBrush", palette.Floating);
        var backgroundRgb = Convert.ToUInt32(background[1..], 16);
        return MarkdownPreview.NormalizeTheme(new(dark, highContrast, background,
            Color("FilesMate.Text.PrimaryBrush", palette.Text),
            Color("FilesMate.Text.SecondaryBrush", palette.Muted),
            Color("FilesMate.SettingsCard.BackgroundBrush", palette.Card),
            Color("FilesMate.Divider.Brush", palette.Outline), Color("FilesMate.Glass.AccentBrush", palette.Accent),
            Color("FilesMate.Item.SelectedBrush", palette.Selected, backgroundRgb),
            Color("FilesMate.Item.SelectedForegroundBrush", palette.SelectedInk)));
    }

    private MarkdownReaderTheme BeginReaderDocument(WebView2 view, long generation, bool pdf = false)
    {
        _readerThemes.Reset();
        _readerView = view; _readerGeneration = generation; _readerIsPdf = pdf; _readerDocumentStamp = null;
        var theme = CaptureReaderTheme();
        _readerThemes.Request(theme);
        return theme;
    }

    private void ResetReaderDocument()
    {
        _readerView = null; _readerDocumentStamp = null;
        _readerThemes.Reset();
    }

    private void ReaderNavigationStarting(WebView2 view, long generation, ulong navigation)
    {
        if (!ReferenceEquals(_readerView, view) || generation != _generation || generation != _readerGeneration) return;
        _readerDocumentStamp = null;
        _readerThemes.BeginNavigation(navigation);
        _readerThemes.Request(CaptureReaderTheme());
    }

    private async void ReaderNavigationCompleted(WebView2 view, long generation, ulong navigation)
    {
        if (!ReferenceEquals(_readerView, view) || generation != _generation || generation != _readerGeneration) return;
        if (!_readerThemes.IsNavigation(navigation)) return;
        var document = _readerThemes.Document;
        try
        {
            var json = await view.CoreWebView2.ExecuteScriptAsync(ReaderThemeDocument.CaptureStampScript(_readerIsPdf, Guid.NewGuid().ToString("D")));
            if (!ReferenceEquals(_readerView, view) || _readerGeneration != _generation || document != _readerThemes.Document || !_readerThemes.IsNavigation(navigation)) return;
            if (!ReaderThemeDocument.TryReadStamp(json, out var stamp)) return;
            _readerDocumentStamp = stamp;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { return; }
        _readerThemes.MarkNavigationReady(navigation);
        RefreshReaderDocumentTheme();
    }

    private async void RefreshReaderDocumentTheme()
    {
        if (_readerView is not { } view || !ReferenceEquals(_pdfContent, view) || _readerGeneration != _generation) return;
        if (_readerDocumentStamp is null) return;
        _readerThemes.Request(CaptureReaderTheme());
        while (_readerThemes.TryTake(out var update))
        {
            var applied = false;
            try
            {
                if (!ReferenceEquals(_readerView, view) || !ReferenceEquals(_pdfContent, view)
                    || update.Document != _readerThemes.Document || _readerGeneration != _generation) return;
                var core = view.CoreWebView2;
                core.Profile.PreferredColorScheme = update.Theme.Dark
                    ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                    : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
                applied = await core.ExecuteScriptAsync((_readerIsPdf ? PdfPreviewTheme.ThemeUpdateScript(update.Theme, _readerDocumentStamp!) : MarkdownPreview.ThemeUpdateScript(update.Theme, _readerDocumentStamp!))) == "true";
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                // Closing or replacing a browser must not reopen the document.
                return;
            }
            finally { _readerThemes.Complete(update, applied); }
            if (!ReferenceEquals(_readerView, view)) return;
        }
    }
}
