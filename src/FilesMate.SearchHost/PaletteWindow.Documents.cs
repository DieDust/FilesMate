using Loc = FilesMate.App.Localization.StringTable;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using FilesMate.App.Preview;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private WebView2? _documentPreview;
    internal string DocumentPreviewStatus { get; private set; } = "";

    private void ReleaseDocumentPreview()
    {
        ReleaseNativeOffice();
        var view = _documentPreview;
        _documentPreview = null;
        if (view is null) return;
        PreviewContentHost.Children.Remove(view);
        view.Dispose();
    }

    private async Task ShowDocumentPreviewAsync(string path, string? markdown, CancellationToken token, string? officeHtml = null)
    {
        var surface = ((SolidColorBrush)FindResource("Surface")).Color;
        // A native child window remains visible with WPF software rendering.
        // CompositionControl's internal capture can succeed while its image is blank.
        var view = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, surface.R, surface.G, surface.B),
            AllowExternalDrop = false,
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(_host.Profile, "document-preview"),
            },
        };
        _documentPreview = view;
        view.AddHandler(System.Windows.Input.Keyboard.KeyDownEvent, new System.Windows.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.Space)
            { e.Handled = true; Dispatcher.BeginInvoke(new Action(() => { if (_documentPreview == view) ClearPreview(); })); }
        }), true);
        PreviewContentHost.Children.Insert(0, view);
        try
        {
            await view.EnsureCoreWebView2Async();
            token.ThrowIfCancellationRequested();
            if (_documentPreview != view) return;
            var core = view.CoreWebView2;
            core.Profile.PreferredColorScheme = PaletteAppearance.IsDark(_appearance)
                ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            core.Settings.AreDevToolsEnabled = false;
            var interactivePdf = markdown is null && officeHtml is null;
            core.Settings.IsScriptEnabled = interactivePdf;
            core.Settings.IsWebMessageEnabled = interactivePdf;
            core.WebMessageReceived += (_, e) =>
            {
                if (PdfPreviewAssets.IsCloseMessage(e.Source, e.WebMessageAsJson)) Dispatcher.BeginInvoke(new Action(() => { if (_documentPreview == view) ClearPreview(); }));
            };
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Save | CoreWebView2PdfToolbarItems.SaveAs
                | CoreWebView2PdfToolbarItems.Print | CoreWebView2PdfToolbarItems.FullScreen;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            var pdfUri = PdfPreviewAssets.ViewerUri;
            core.NavigationStarting += (_, e) =>
            {
                DocumentPreviewStatus = e.Uri.StartsWith("data:", StringComparison.Ordinal) ? "Generated Markdown" : "Document navigation";
                var allowed = officeHtml is not null ? e.Uri.Split('#')[0] == "https://filesmate-office.local/preview.html" : markdown is null ? e.Uri.Split('#')[0] == pdfUri : MarkdownPreview.IsDocumentNavigation(e.Uri, e.IsUserInitiated);
                if (allowed) return;
                e.Cancel = true;
                if (e.IsUserInitiated && Uri.TryCreate(e.Uri, UriKind.Absolute, out var link) && link.Host != "filesmate-preview.local" && link.Scheme is "https" or "http")
                {
                    try { FilesMate.Platform.Windows.Processes.DetachedProcess.Open(link.AbsoluteUri); }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                    { PreviewNote.Text = Loc.Get("Preview_LinkFailed"); }
                }
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (_documentPreview != view || token.IsCancellationRequested) return;
                DocumentPreviewStatus += $"; completed={e.IsSuccess}/{e.WebErrorStatus}";
                // Native browser surfaces cover WPF siblings, including error text.
                if (!e.IsSuccess) ReleaseDocumentPreview();
                PreviewMessage.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                PreviewMessage.Text = Loc.Get("Preview_UnavailableShort");
            };
            core.ProcessFailed += (_, _) =>
            {
                if (_documentPreview != view) return;
                ReleaseDocumentPreview();
                PreviewMessage.Text = Loc.Get("Preview_Interrupted");
                PreviewMessage.Visibility = Visibility.Visible;
            };
            if (officeHtml is not null)
            {
                const string officeUri = "https://filesmate-office.local/preview.html";
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    var allowed = e.Request.Uri.Split('#')[0] == officeUri;
                    e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(allowed ? officeHtml : "")), allowed ? 200 : 403, allowed ? "OK" : "Forbidden", "Content-Type: text/html; charset=utf-8");
                };
                core.Navigate(officeUri);
            }
            else if (markdown is null)
            {
                using (PdfPreviewAssets.OpenDocument(path)) { }
                core.SetVirtualHostNameToFolderMapping(PdfPreviewAssets.Host, PdfPreviewAssets.Folder, CoreWebView2HostResourceAccessKind.DenyCors);
                core.AddWebResourceRequestedFilter(PdfPreviewAssets.DocumentUri, CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    try
                    {
                        var response = PdfPreviewAssets.OpenResponse(path, e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null);
                        e.Response = core.Environment.CreateWebResourceResponse(response.Body, response.Status, response.Status == 206 ? "Partial Content" : "OK", response.Headers);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", ""); }
                };
                core.Navigate(pdfUri);
            }
            else
            {
                core.SetVirtualHostNameToFolderMapping("filesmate-preview.local", Path.GetDirectoryName(Path.GetFullPath(path))!, CoreWebView2HostResourceAccessKind.DenyCors);
                string Color(string name) { var color = ((SolidColorBrush)FindResource(name)).Color; return $"#{color.R:X2}{color.G:X2}{color.B:X2}"; }
                var dark = PaletteAppearance.IsDark(_appearance);
                var background = Color("Surface"); var foreground = Color("Ink"); var accent = Color("Accent");
                var html = await Task.Run(() => MarkdownPreview.Render(markdown, dark, background, foreground, accent), token);
                token.ThrowIfCancellationRequested();
                core.NavigateToString(html);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or ArgumentException)
        {
            if (_documentPreview != view || token.IsCancellationRequested) return;
            ReleaseDocumentPreview();
            PreviewMessage.Text = error is IOException ? error.Message : Loc.Get("Preview_WebViewFailed");
            PreviewMessage.Visibility = Visibility.Visible;
        }
    }
}
