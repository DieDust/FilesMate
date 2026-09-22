#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Views;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunReviewPreviewSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            NavigatorPage? page = null;
            for (var i = 0; i < 200; i++)
            {
                if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { Navigator: { IsLoaded: true } candidate } }
                    && !candidate.ViewModel.IsLoading && candidate.ViewModel.ItemCount > 0) { page = candidate; break; }
                await Task.Delay(100);
            }
            if (page is null) throw new IOException("File view unavailable.");
            var surface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", flags)!.GetValue(page)!;
            if (!surface.TrySelectByName("reading.pdf")) throw new IOException("Fixture missing.");
            ((EventHandler)typeof(FileDetailsSurface).GetField("QuickPreviewRequested", flags)!.GetValue(surface)!)(surface, EventArgs.Empty);
            var card = (QuickPreviewWindow?)typeof(NavigatorPage).GetField("_quickPreview", flags)!.GetValue(page)
                ?? throw new IOException("Preview did not open.");
            card.AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            var pane = (PreviewPane)typeof(QuickPreviewWindow).GetField("_pane", flags)!.GetValue(card)!;
            WebView2? View() => (WebView2?)typeof(PreviewPane).GetField("_pdfContent", flags)!.GetValue(pane);
            async Task Ready()
            {
                for (var i = 0; i < 300; i++)
                {
                    if (View()?.CoreWebView2 is { } core && await core.ExecuteScriptAsync("document.body.dataset.ready==='true'") == "true") return;
                    await Task.Delay(100);
                }
                throw new IOException("PDF failed to render.");
            }
            await Ready();
            foreach (var number in new[] { 10000, 5000, 1 })
            {
                await View()!.CoreWebView2.ExecuteScriptAsync($"document.getElementById('page').value={number};document.getElementById('page').dispatchEvent(new Event('change'));");
                await Ready();
                var bounded = await View()!.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.pdf-page').length<=9 && document.querySelectorAll('canvas').length<=3");
                if (bounded != "true") throw new IOException("PDF DOM exceeds viewport bounds.");
                await View()!.CoreWebView2.ExecuteScriptAsync("document.getElementById('plus').click()");
                await Ready();
            }
            result["Pdf10000PagesJumpZoom"] = true;
            await View()!.CoreWebView2.ExecuteScriptAsync("window.chrome.webview.postMessage({command:'close-preview'})");
            await Task.Delay(100);
            if (View() is null) throw new IOException("Untrusted message shape closed the preview.");
            await View()!.CoreWebView2.ExecuteScriptAsync("window.chrome.webview.postMessage('close-preview')");
            await Task.Delay(300);
            if (View() is not null) throw new IOException("PDF renderer was retained after close.");
            result["MessageValidationAndRelease"] = true;
            var office = Environment.GetEnvironmentVariable("FILESMATE_REVIEW_OFFICE")!;
            var converted = await new Preview.Providers.OfficePreviewProvider().CreateAsync(new Preview.PreviewRequest(office, 1));
            if (converted is not Preview.PreviewResult.Html) throw new IOException("Bounded Office worker failed: " + converted);
            result["ManagedOfficeConversion"] = true;
            result["Passed"] = true;
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "review-preview-smoke.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}
#endif
