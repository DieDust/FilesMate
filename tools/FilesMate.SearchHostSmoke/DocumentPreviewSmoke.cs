using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FilesMate.Search;
using FilesMate.SearchHost;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class DocumentPreviewSmoke
{
    internal static async Task RunAsync(Window window, string profile, Action show, Action<GlobalSearchSettings> apply, bool realInput = false)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        object? Invoke(string method, params object[] values) => window.GetType().GetMethod(method, flags)!.Invoke(window, values);
        WebView2? View() => (WebView2?)window.GetType().GetField("_documentPreview", flags)!.GetValue(window);
        var card = (Border)window.FindName("PreviewCard");
        var status = (TextBlock)window.FindName("PreviewMessage");
        void CaptureVisibleDocument(FrameworkElement document, string name)
        {
            var origin = document.PointToScreen(new Point(6, 6));
            var corner = document.PointToScreen(new Point(document.ActualWidth - 6, document.ActualHeight - 6));
            Assert(InputProbe.OwnsRootPoint(new Point((origin.X + corner.X) / 2, (origin.Y + corner.Y) / 2)), "Another window covers the document capture");
            using var capture = new System.Drawing.Bitmap((int)(corner.X - origin.X), (int)(corner.Y - origin.Y));
            using (var graphics = System.Drawing.Graphics.FromImage(capture))
                graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, capture.Size);
            capture.Save(Path.Combine(profile, name + "-screen.png"));
            var surface = ((SolidColorBrush)window.FindResource("Surface")).Color;
            int changed = 0, samples = 0;
            for (int y = 8; y < capture.Height - 8; y += 4)
                for (int x = 8; x < capture.Width - 8; x += 4)
                {
                    var pixel = capture.GetPixel(x, y); samples++;
                    if (Math.Abs(pixel.R - surface.R) + Math.Abs(pixel.G - surface.G) + Math.Abs(pixel.B - surface.B) > 90) changed++;
                }
            Assert(changed > samples * .01, "Document rendered internally but the actual screen is blank: " + name);
            var cardOrigin = card.PointToScreen(new Point());
            var cardCorner = card.PointToScreen(new Point(card.ActualWidth, card.ActualHeight));
            using var full = new System.Drawing.Bitmap((int)(cardCorner.X - cardOrigin.X), (int)(cardCorner.Y - cardOrigin.Y));
            using (var graphics = System.Drawing.Graphics.FromImage(full)) graphics.CopyFromScreen((int)cardOrigin.X, (int)cardOrigin.Y, 0, 0, full.Size);
            full.Save(Path.Combine(profile, name + "-card-screen.png"));
        }
        async Task Wait(Func<bool> predicate)
        {
            for (int n = 0; n < 200 && !predicate(); n++) await Task.Delay(100);
            Assert(predicate(), "Document preview timed out: " + status.Text + $"; visible={window.IsVisible}; card={card.IsVisible}; view={View()?.CoreWebView2?.Source}; navigation={window.GetType().GetProperty("DocumentPreviewStatus", flags)!.GetValue(window)}; focus={window.GetType().GetProperty("LastFocusDismiss", flags)!.GetValue(window)}");
        }
        var markdown = Path.Combine(profile, "阅读预览 示例.markdown");
        File.WriteAllText(markdown, "# FilesMate Reading\n\n**Bold** and *italic*, rendered as a document.\n\n- [x] PDF preview\n- [ ] Read next\n\n|Format|Preview|\n|---|---|\n|PDF|Pages|\n|Markdown|Reading|\n\n```csharp\nvar preview = true;\n```\n\n" + string.Join("\n\n", Enumerable.Range(1, 60).Select(i => $"Paragraph {i}: scroll through the document.")));
        var pdf = Path.Combine(profile, "阅读预览 示例.pdf");
        WritePdf(pdf);
        using (var picture = new System.Drawing.Bitmap(40, 40))
        {
            using var graphics = System.Drawing.Graphics.FromImage(picture);
            graphics.Clear(System.Drawing.Color.CornflowerBlue);
            picture.Save(Path.Combine(profile, "local-picture.png"));
        }
        File.AppendAllText(markdown, "\n\n![Local picture](local-picture.png)\n");
        var icon = new DrawingImage();
        SearchRow Row(string path) => new(new NameHit(Path.GetFileName(path), path, false), icon);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            Invoke("Dismiss");
            File.WriteAllText(Path.Combine(profile, "appearance.json"), "{\"theme\":\"" + theme + "\"}");
            show();
            // This rendering test does not take real mouse/keyboard input from the user.
            window.GetType().GetField("_opening", flags)!.SetValue(window, true);
            window.GetType().GetField("_contextOpen", flags)!.SetValue(window, true);
            ((TextBox)window.FindName("QueryBox")).Text = "fixture";
            await Wait(() => ((ListBox)window.FindName("Results")).Items.Count > 0 && !(bool)window.GetType().GetField("_pending", flags)!.GetValue(window)!);
            await Task.Delay(400);
            Assert(View() is null && !card.IsVisible, "Search refresh loaded a preview without selection");
            var results = (ListBox)window.FindName("Results");
            window.GetType().GetField("_contextOpen", flags)!.SetValue(window, false);
            void HoverGrids(DependencyObject element)
            {
                if (element is Grid { DataContext: SearchRow } grid)
                    grid.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) HoverGrids(VisualTreeHelper.GetChild(element, i));
            }
            HoverGrids(results);
            await Task.Delay(400);
            Assert(!card.IsVisible, "Hovering results loaded a preview");
            results.SelectedIndex = 1;
            await Wait(() => card.IsVisible && ((ICSharpCode.AvalonEdit.TextEditor)window.FindName("PreviewText")).Text.StartsWith("fixture"));
            Invoke("ClearPreview");
            window.GetType().GetField("_contextOpen", flags)!.SetValue(window, false);
            Invoke("QueuePreview", Row(markdown), 0);
            await Wait(() => View()?.CoreWebView2 is not null && status.Visibility == Visibility.Collapsed);
            var view = View()!;
            Assert(window.Width <= 660 && card.IsVisible, "Document changed the search width or failed to show");
            var dom = await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({heading:document.querySelector('h1')?.textContent,table:!!document.querySelector('table'),bold:!!document.querySelector('strong'),code:!!document.querySelector('pre code'),scheme:getComputedStyle(document.documentElement).colorScheme})");
            Assert(dom.Contains("FilesMate Reading") && dom.Contains("true") && dom.Contains(theme.ToLowerInvariant()), "Markdown DOM/theme missing: " + dom);
            Assert(await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('img').naturalWidth === 40") == "true", "Local Markdown image did not load");
            await Task.Delay(500);
            CaptureVisibleDocument(view, "markdown-" + theme);
            if (theme == "Light")
            {
                var content = (Grid)card.Child;
                var handles = content.Children.OfType<System.Windows.Controls.Primitives.Thumb>().ToArray();
                Assert(handles.Length == 8, "Preview must expose all four edges and corners");
                foreach (var handle in handles)
                {
                    var edge = (string)handle.Tag;
                    var before = new Size(card.ActualWidth, card.ActualHeight);
                    Invoke("PreviewResize_Started", handle, new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
                    Invoke("PreviewResize_Delta", handle, new System.Windows.Controls.Primitives.DragDeltaEventArgs(edge.Contains('L') ? 8 : edge.Contains('R') ? -8 : 0, edge.Contains('T') ? 8 : edge.Contains('B') ? -8 : 0));
                    Invoke("PreviewResize_Completed", handle, new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false));
                    await Task.Delay(80);
                    Assert(card.ActualWidth < before.Width || card.ActualHeight < before.Height, "Resize handle did not change dimensions: " + edge);
                    var restored = SearchPreviewSize.Load(profile)!;
                    Assert(Math.Abs(restored.Width - card.Width) < 1 && Math.Abs(restored.Height - card.Height) < 1, "Preview size was not saved");
                }
                var savedSize = SearchPreviewSize.Load(profile)!;
                Invoke("ClearPreview");
                Invoke("QueuePreview", Row(markdown), 0);
                await Wait(() => View()?.CoreWebView2 is not null && status.Visibility == Visibility.Collapsed);
                view = View()!;
                Assert(Math.Abs(card.ActualWidth - savedSize.Width) < 1 && Math.Abs(card.ActualHeight - savedSize.Height) < 1, "Reopening did not restore preview size");
                CaptureVisibleDocument(view, "markdown-resized");
            }
            await using (var screenshot = File.Create(Path.Combine(profile, "markdown-" + theme + ".png")))
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            await view.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0,800)");
            Assert(await view.CoreWebView2.ExecuteScriptAsync("window.scrollY > 0") == "true", "Markdown does not scroll");
            Invoke("QueuePreview", Row(pdf), 0);
            await Wait(() => View() is { } current && current != view && current.CoreWebView2 is not null && status.Visibility == Visibility.Collapsed);
            var pdfView = View()!;
            Assert(pdfView.CoreWebView2.Source.StartsWith("file:///", StringComparison.Ordinal), "PDF did not navigate to the local document");
            await Task.Delay(1500);
            CaptureVisibleDocument(pdfView, "pdf-" + theme);
            await using (var screenshot = File.Create(Path.Combine(profile, "pdf-" + theme + ".png")))
                await pdfView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            Assert(pdfView.CoreWebView2.Profile.PreferredColorScheme == (theme == "Dark" ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light), "PDF chrome theme differs");
            await pdfView.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mouseWheel\",\"x\":140,\"y\":140,\"deltaY\":650,\"deltaX\":0}");
            await Task.Delay(1500);
            await using (var screenshot = File.Create(Path.Combine(profile, "pdf-page2-" + theme + ".png")))
                await pdfView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            Invoke("QueuePreview", Row(markdown), 0);
            Invoke("ClearPreview");
            await Task.Delay(300);
            Assert(View() is null && !((System.Windows.Controls.Primitives.Popup)window.FindName("PreviewPopup")).IsOpen, "Canceled document preview reappeared");
        }
        var saved = GlobalSearchConfiguration.Load(profile);
        apply(saved with { PreviewEnabled = false });
        Invoke("QueuePreview", Row(markdown), 0);
        await Task.Delay(250);
        Assert(View() is null && !card.IsVisible && !GlobalSearchConfiguration.Load(profile).PreviewEnabled, "Disabled preview still loaded content or was not saved");
        Invoke("ShowSettings", true, "");
        var toggle = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("PreviewEnabledBox");
        Assert(toggle.IsChecked == false, "Settings switch does not reflect saved opt-out");
        toggle.IsChecked = true;
        Assert(GlobalSearchConfiguration.Load(profile).PreviewEnabled, "Preview switch did not autosave");
        Invoke("ShowSettings", false, "");
        Invoke("QueuePreview", Row(markdown), 0);
        await Wait(() => View()?.CoreWebView2 is not null && status.Visibility == Visibility.Collapsed);
        apply(saved with { PreviewEnabled = false });
        Assert(View() is null && !card.IsVisible, "Turning off preview retained the browser");
        if (realInput)
        {
            apply(saved with { PreviewEnabled = true });
            Invoke("QueuePreview", Row(markdown), 0);
            await Wait(() => View()?.CoreWebView2 is not null && status.Visibility == Visibility.Collapsed);
            var pointer = System.Windows.Forms.Cursor.Position;
            try
            {
                var header = window.PointToScreen(new Point(25, 25));
                Assert(InputProbe.OwnsRootPoint(header), "Another window covers the test header");
                InputProbe.Click(header, false);
                window.GetType().GetField("_opening", flags)!.SetValue(window, false);
                window.GetType().GetField("_contextOpen", flags)!.SetValue(window, false);
                var handle = ((Grid)card.Child).Children.OfType<System.Windows.Controls.Primitives.Thumb>().Single(t => (string)t.Tag == "RB");
                var start = handle.PointToScreen(new Point(6, 6));
                Assert(InputProbe.OwnsRootPoint(start), "Another window covers the resize handle");
                var previousWidth = card.ActualWidth;
                await Task.Run(() => InputProbe.Drag(start, new Point(start.X - 28, start.Y - 28)));
                await Task.Delay(200);
                Assert(window.IsVisible && card.ActualWidth < previousWidth - 5, "Real corner drag did not resize preview");
                var inside = View()!.PointToScreen(new Point(70, 65));
                Assert(InputProbe.OwnsRootPoint(inside), "Another window covers the document click target");
                InputProbe.Click(inside, false);
                await Task.Delay(250);
                Assert(window.IsVisible && card.IsVisible, "Clicking native document dismissed search");
                Assert(InputProbe.OwnsRootPoint(inside), "Document lost ownership before keyboard test");
                InputProbe.Key(0x1B);
                await Task.Delay(300);
                Assert(!window.IsVisible && View() is null, "Esc from native document did not dismiss search");
            }
            finally { System.Windows.Forms.Cursor.Position = pointer; }
        }
        Invoke("Dismiss");
        Assert(View() is null, "Dismiss retained browser control");
        File.WriteAllText(Path.Combine(profile, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = true, ActualScreenContent = true, BothThemes = true, AllEightResizeHandles = true, SizePersists = true, RealDragAndEscape = realInput, MarkdownScroll = true, LocalImages = true, PdfNavigation = true, NoHoverOrAutomaticPreview = true, PreviewToggleAutosaves = true, StalePreviewCancelled = true, DismissReleases = true }));
    }

    private static void WritePdf(string path)
    {
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 420] /Resources << /Font << /F1 7 0 R >> >> /Contents 4 0 R >>",
            Stream("BT /F1 20 Tf 30 350 Td (FilesMate PDF) Tj 0 -40 Td (Page one) Tj ET"),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 420] /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>",
            Stream("BT /F1 20 Tf 30 350 Td (Page two) Tj ET"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[objects.Length];
        for (int i = 0; i < objects.Length; i++) { offsets[i] = text.Length; text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length;
        text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer << /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, text.ToString(), Encoding.ASCII);
        static string Stream(string data) => $"<< /Length {data.Length} >>\nstream\n{data}\nendstream";
    }
}
