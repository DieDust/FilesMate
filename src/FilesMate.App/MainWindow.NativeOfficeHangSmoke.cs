#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Preview;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunNativeOfficeHangSmokeAsync()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var result = new Dictionary<string, object>();
        QuickPreviewWindow? card = null;
        try
        {
            card = new QuickPreviewWindow(this);
            card.AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            card.Activate();
            await card.LoadAsync(@"D:\FilesMate\artifacts\office-fixtures\预算表.xlsx");
            var pane = (PreviewPane)typeof(QuickPreviewWindow).GetField("_pane", flags)!.GetValue(card)!;
            NativeOfficePreview? Native() => (NativeOfficePreview?)typeof(PreviewPane).GetField("_nativeOffice", flags)!.GetValue(pane);
            var native = Native() ?? throw new Exception("Native preview unavailable");
            // With the test UIA query blocked forever, the STA and main window
            // must remain alive beyond the four-second watchdog budget.
            var pulses = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += (_, _) => pulses++;
            timer.Start();
            await Task.Delay(5000);
            timer.Stop();
            if (!native.ToolbarProbeTimedOut) throw new Exception("UIA stall injection did not exercise timeout");
            if (!native.IsAlive || pulses < 40) throw new Exception("UIA probe blocked a window dispatcher");
            var width = card.AppWindow.Size.Width;
            card.Activate();
            card.AppWindow.Resize(new Windows.Graphics.SizeInt32(width + 80, card.AppWindow.Size.Height + 40));
            await Task.Delay(400);
            GetClientRect(native.Window, out var bounds);
            if (bounds.Right < width - 60) throw new Exception("Resize failed while UIA probe stalled");
            result["BackgroundProbeDoesNotBlock"] = true;
            result["UiPulses"] = pulses;
            var workerPid = native.ProcessId;
            var watch = Stopwatch.StartNew();
            native.SimulateStallForTest();
            await Task.Delay(250);
            card.Activate();
            for (var attempt = 0; attempt < 150; attempt++)
            {
                var view = (WebView2?)typeof(PreviewPane).GetField("_pdfContent", flags)!.GetValue(pane);
                if (Native() is null && view?.CoreWebView2 is { } core
                    && await core.ExecuteScriptAsync("document.querySelectorAll('table').length>0") == "true") break;
                if (attempt == 149) throw new Exception("Hung native worker did not fall back");
                await Task.Delay(100);
            }
            result["HungWorkerFallbackMilliseconds"] = watch.ElapsedMilliseconds;
            if (watch.ElapsedMilliseconds > 12000) throw new Exception("Hang recovery exceeded bounded deadline");
            try { using var worker = Process.GetProcessById(workerPid); if (!worker.HasExited) throw new Exception("Hung worker survived"); }
            catch (ArgumentException) { }
            result["WatchdogReleasedWorker"] = true;
            var browser = ((WebView2)typeof(PreviewPane).GetField("_pdfContent", flags)!.GetValue(pane)!).CoreWebView2.BrowserProcessId;
            card.Close();
            var release = Stopwatch.StartNew();
            while (true)
            {
                try { using var process = Process.GetProcessById((int)browser); if (process.HasExited) break; }
                catch (ArgumentException) { break; }
                if (release.ElapsedMilliseconds > 15000) throw new Exception("WebView browser retained after card closed");
                await Task.Delay(100);
            }
            result["CloseReleasedWebViewBrowserMilliseconds"] = release.ElapsedMilliseconds;
            result["Passed"] = true;
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        finally { card?.Close(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "native-office-hang-smoke.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
#endif
