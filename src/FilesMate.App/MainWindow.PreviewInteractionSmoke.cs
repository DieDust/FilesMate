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
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out NativePreviewRect rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePreviewRect { public int Left, Top, Right, Bottom; }
    private async Task RunPreviewInteractionSmokeAsync()
    {
        var result=new Dictionary<string,object>();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000,-10000));
            NavigatorPage? page=null;
            for(var i=0;i<200;i++)
            {
                if(Tabs.SelectedItem is TabViewItem {Tag:NavigatorTabContent {Navigator:{IsLoaded:true} candidate}} && !candidate.ViewModel.IsLoading && candidate.ViewModel.ItemCount>0){page=candidate;break;}
                await Task.Delay(100);
            }
            if(page is null)throw new Exception("File view not ready");
            var surface=(FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface",flags)!.GetValue(page)!;
            QuickPreviewWindow? Card()=>(QuickPreviewWindow?)typeof(NavigatorPage).GetField("_quickPreview",flags)!.GetValue(page);
            var before=surface.ActualWidth;
            if (Environment.GetEnvironmentVariable("FILESMATE_NATIVE_OFFICE_HANG_SMOKE") == "1")
            {
                await RunNativeOfficeHangSmokeAsync();
                return;
            }
            if(!surface.TrySelectByName("reading.pdf"))throw new Exception("PDF fixture missing");
            var toggle=(EventHandler)typeof(FileDetailsSurface).GetField("QuickPreviewRequested",flags)!.GetValue(surface)!;
            if (Environment.GetEnvironmentVariable("FILESMATE_NATIVE_OFFICE_VISUAL") == "1")
            {
                await RunNativeOfficeVisualSmokeAsync();
                return;
            }
            if (Environment.GetEnvironmentVariable("FILESMATE_PREVIEW_MANUAL_FILE") is {} manualPath)
            {
                if (!surface.TrySelectByName(Path.GetFileName(manualPath))) throw new Exception("Manual preview fixture missing");
                await Task.Delay(300);
                toggle(surface, EventArgs.Empty);
                var manual = Card() ?? throw new Exception("Manual preview did not open");
                await manual.LoadAsync(manualPath);
                manual.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(100, 100, 1000, 820));
                manual.Activate();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-capture-handle.txt"), WinRT.Interop.WindowNative.GetWindowHandle(manual).ToString());
                return;
            }
            toggle(surface,EventArgs.Empty);
            var card=Card()??throw new Exception("Space did not open preview card");
            card.AppWindow.Move(new Windows.Graphics.PointInt32(-10000,-10000));
            var pane=(PreviewPane)typeof(QuickPreviewWindow).GetField("_pane",flags)!.GetValue(card)!;
            var root=(Grid)card.Content;
            await Task.Delay(250);
            if(root.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush brush || brush.Color.A!=255)throw new Exception("Preview background must be opaque themed surface");
            if(root.ActualWidth>610 || root.ActualHeight>510)throw new Exception("Preview default dimensions too large");
            var opacity=typeof(QuickPreviewWindow).GetField("_opacity",flags)!;
            for(var i=0;i<20 && (byte)opacity.GetValue(card)! != 255;i++)await Task.Delay(50);
            if((byte)opacity.GetValue(card)! != 255)throw new Exception("Opening fade did not finish");
            result["ThemedCompactCard"]=true;result["OpeningFadeCompleted"]=true;
            WebView2? View()=>(WebView2?)typeof(PreviewPane).GetField("_pdfContent",flags)!.GetValue(pane);
            for(var i=0;i<200;i++)
            {
                await Task.Delay(100);
                if(View()?.CoreWebView2 is { } core && await core.ExecuteScriptAsync("document.body.dataset.ready==='true'")=="true")break;
                if(i==199)throw new Exception("Card PDF failed to render");
            }
            if(Math.Abs(surface.ActualWidth-before)>1)throw new Exception("Quick preview resized folder columns");
            result["SpaceOpensIndependentCard"]=true;result["PdfRendered"]=true;
            if(Environment.GetEnvironmentVariable("FILESMATE_PDF_REGRESSION") is {} actualPdf)
            {
                await card.LoadAsync(actualPdf);
                for(var i=0;i<300;i++)
                {
                    await Task.Delay(100);
                    if(View()?.CoreWebView2 is {} real && await real.ExecuteScriptAsync("document.body.dataset.ready==='true'")=="true")break;
                    if(i==299)throw new Exception("Large PDF card did not render");
                }
                result["LargePdfCardRendered"]=true;
            }
            if(Environment.GetEnvironmentVariable("FILESMATE_PREVIEW_VISIBLE_PDF")=="1")
            {
                card.AppWindow.Move(new Windows.Graphics.PointInt32(120,120));
                await Task.Delay(800);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"preview-capture-handle.txt"),WinRT.Interop.WindowNative.GetWindowHandle(card).ToString());
                await Task.Delay(6000);
            }
            await View()!.CoreWebView2.ExecuteScriptAsync("setTimeout(()=>window.chrome.webview.postMessage('close-preview'),30)");
            await Task.Delay(300);
            if(Card() is not null||pane.IsPreviewVisible||View()is not null)throw new Exception("Closing retained card or renderer");
            result["CloseReleasesRenderer"]=true;
            surface.TrySelectByName("long-preview.log");toggle(surface,EventArgs.Empty);
            card=Card()??throw new Exception("Text card failed");card.AppWindow.Move(new Windows.Graphics.PointInt32(-10000,-10000));
            pane=(PreviewPane)typeof(QuickPreviewWindow).GetField("_pane",flags)!.GetValue(card)!;
            await Task.Delay(800);
            var text=(ListView)typeof(PreviewPane).GetField("TextContent",flags)!.GetValue(pane)!;
            var scroll=(ScrollViewer?)typeof(PreviewPane).GetField("_textScroll",flags)!.GetValue(pane);
            if(scroll is null||text.Items.Count!=256)throw new Exception("Text initial load is not bounded: "+text.Items.Count);
            scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await Task.Delay(500);
            if(text.Items.Count<=256||scroll.VerticalOffset<=0)throw new Exception("Text scroll did not append without jumping");
            var realized=0;for(var i=0;i<text.Items.Count;i++)if(text.ContainerFromIndex(i)is not null)realized++;
            if(realized>=text.Items.Count/2)throw new Exception("Text rows are not virtualized: "+realized);
            result["TextContinuousScroll"]=true;result["RealizedTextRows"]=realized;
            static TextBlock? FindLine(Microsoft.UI.Xaml.DependencyObject node)
            {
                if(node is TextBlock line && line.Text.StartsWith("日志"))return line;
                for(var i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);i++)
                    if(FindLine(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node,i)) is {} child)return child;
                return null;
            }
            var line=FindLine(text)??throw new Exception("No text row realized");
            result["TextForeground"]=line.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush foreground?foreground.Color.ToString():"other";
            result["TextTheme"]=line.ActualTheme.ToString();result["TextRowSize"]=$"{line.ActualWidth}x{line.ActualHeight}";
            if(Environment.GetEnvironmentVariable("FILESMATE_PREVIEW_VISIBLE_CHECK")=="1")
            {
                card.AppWindow.Move(new Windows.Graphics.PointInt32(120,120));
                await Task.Delay(800);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"preview-capture-handle.txt"),WinRT.Interop.WindowNative.GetWindowHandle(card).ToString());
                await Task.Delay(6000);
            }
            await Capture((Microsoft.UI.Xaml.UIElement)card.Content,"preview-flow-card.png");
            toggle(surface,EventArgs.Empty);await Task.Delay(250);
            if (Environment.GetEnvironmentVariable("FILESMATE_NATIVE_OFFICE_SMOKE") == "1")
            {
                surface.TrySelectByName("embedded.xls");
                await Task.Delay(200);
                toggle(surface, EventArgs.Empty);
                card = Card() ?? throw new Exception("Native Office card did not open");
                card.AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
                pane = (PreviewPane)typeof(QuickPreviewWindow).GetField("_pane", flags)!.GetValue(card)!;
                FilesMate.App.Preview.NativeOfficePreview? NativeOffice() => (FilesMate.App.Preview.NativeOfficePreview?)typeof(PreviewPane).GetField("_nativeOffice", flags)!.GetValue(pane);
                for (var i = 0; i < 160 && NativeOffice() is null; i++) await Task.Delay(100);
                var native = NativeOffice() ?? throw new Exception("Installed native Office previewer failed");
                result["NativeOfficeOpened"] = native.IsAlive;
                await Task.Delay(200);
                GetClientRect(native.Window, out var originalNativeBounds);
                card.AppWindow.Resize(new Windows.Graphics.SizeInt32(card.AppWindow.Size.Width + 100, card.AppWindow.Size.Height + 60));
                await Task.Delay(250);
                GetClientRect(native.Window, out var resizedNativeBounds);
                if (resizedNativeBounds.Right <= originalNativeBounds.Right || resizedNativeBounds.Bottom <= originalNativeBounds.Bottom) throw new Exception("Native content did not follow card resize");
                typeof(PreviewPane).GetMethod("InfoTab_Click", flags)!.Invoke(pane, [pane, new Microsoft.UI.Xaml.RoutedEventArgs()]);
                await Task.Delay(150);
                if (IsWindowVisible(native.Window)) throw new Exception("Native content covers information tab");
                typeof(PreviewPane).GetMethod("ContentTab_Click", flags)!.Invoke(pane, [pane, new Microsoft.UI.Xaml.RoutedEventArgs()]);
                await Task.Delay(150);
                if (!IsWindowVisible(native.Window)) throw new Exception("Native content did not return after information tab");
                result["NativeResizeAndInformationTab"] = true;
                var workerPid = native.ProcessId;
                // Simulate failure of only our disposable worker; the same card must fall back.
                using (var worker = System.Diagnostics.Process.GetProcessById(workerPid)) worker.Kill();
                for (var i = 0; i < 180; i++)
                {
                    if (View()?.CoreWebView2 is { } html && await html.ExecuteScriptAsync("document.querySelectorAll('table').length>0") == "true") break;
                    await Task.Delay(100);
                    if (i == 179) throw new Exception("Native crash did not fall back to lightweight preview");
                }
                if (NativeOffice() is not null) throw new Exception("Failed native session retained");
                result["NativeCrashFallsBack"] = true;
                // Opening a real document again must create a fresh native session.
                await card.LoadAsync("D:\\FilesMate\\artifacts\\preview-interaction-fixtures\\long-preview.log");
                await card.LoadAsync(Path.Combine("D:\\FilesMate\\artifacts\\preview-interaction-fixtures", "embedded.xls"));
                native = NativeOffice() ?? throw new Exception("Native preview did not reopen");
                typeof(PreviewPane).GetMethod("CompatiblePreview_Click", flags)!.Invoke(pane, [pane, new Microsoft.UI.Xaml.RoutedEventArgs()]);
                for (var i = 0; i < 180; i++)
                {
                    if (View()?.CoreWebView2 is { } html && await html.ExecuteScriptAsync("document.querySelectorAll('table').length>0") == "true") break;
                    await Task.Delay(100);
                    if (i == 179) throw new Exception("Manual compatible preview did not render");
                }
                if (NativeOffice() is not null) throw new Exception("Manual fallback retained native session");
                result["ManualCompatiblePreviewRendered"] = true;
                var modeButton = (Button)typeof(PreviewPane).GetField("CompatiblePreviewButton", flags)!.GetValue(pane)!;
                if (modeButton.Visibility != Microsoft.UI.Xaml.Visibility.Visible) throw new Exception("Return-to-native action missing");
                for (var i = 0; i < 100 && !modeButton.IsEnabled; i++) await Task.Delay(50);
                typeof(PreviewPane).GetMethod("CompatiblePreview_Click", flags)!.Invoke(pane, [pane, new Microsoft.UI.Xaml.RoutedEventArgs()]);
                for (var i = 0; i < 180 && NativeOffice() is null; i++) await Task.Delay(100);
                native = NativeOffice() ?? throw new Exception("Native preview did not reopen after manual fallback");
                if (View() is not null) throw new Exception("Switching back retained compatible renderer");
                result["SwitchBackToNative"] = true;
                workerPid = native.ProcessId;
                card.Close();
                await Task.Delay(2200);
                try { using var worker = System.Diagnostics.Process.GetProcessById(workerPid); if (!worker.HasExited) throw new Exception("Closed card retained native worker"); }
                catch (ArgumentException) { }
                result["NativeCloseReleasesWorker"] = true;
            }
            surface.TrySelectByName("visuals.xlsx");toggle(surface,EventArgs.Empty);
            card=Card()??throw new Exception("Second open failed");card.AppWindow.Move(new Windows.Graphics.PointInt32(-10000,-10000));
            surface.TrySelectByName("visuals.pptx");await Task.Delay(700);
            if(!card.Title.EndsWith("visuals.pptx"))throw new Exception("Selection did not update preview");
            toggle(surface,EventArgs.Empty);await Task.Delay(250);if(Card()is not null)throw new Exception("Repeated Space failed to close");
            result["SelectionFollows"]=true;result["SpaceClosesCard"]=true;result["Passed"]=true;
        }
        catch(Exception error){result["Error"]=error.ToString();result["Passed"]=false;}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"preview-interaction-smoke.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
