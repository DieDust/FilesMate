using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FilesMate.Search;
using FilesMate.SearchHost;
using Microsoft.Web.WebView2.Wpf;

internal static class OfficePreviewSmoke
{
    internal static async Task Run(Window window, string profile, string fixtures)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Call(string method, params object[] args) => window.GetType().GetMethod(method, flags)!.Invoke(window, args);
        WebView2? View() => (WebView2?)window.GetType().GetField("_documentPreview", flags)!.GetValue(window);
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task Wait(Func<bool> predicate)
        {
            for (var i = 0; i < 240 && !predicate(); i++) await Task.Delay(100);
            Assert(predicate(), "Office preview timed out: " + ((TextBlock)window.FindName("PreviewMessage")).Text);
        }
        window.GetType().GetField("_opening", flags)!.SetValue(window, true);
        ((TextBox)window.FindName("QueryBox")).Text = "fixture";
        await Wait(() => ((ListBox)window.FindName("Results")).Items.Count > 2 && !(bool)window.GetType().GetField("_pending", flags)!.GetValue(window)!);
        await Task.Delay(400);
        var status = (TextBlock)window.FindName("PreviewMessage");
        var icon = new DrawingImage();
        foreach (var path in Directory.EnumerateFiles(fixtures).Where(p => new[] { ".doc", ".docx", ".rtf", ".xls", ".xlsx", ".ppt", ".pptx" }.Contains(Path.GetExtension(p))))
        {
            Call("ClearPreview");
            Call("QueuePreview", new SearchRow(new NameHit(Path.GetFileName(path), path, false), icon), 0);
            await Wait(() => View()?.CoreWebView2 is not null && status.Visibility == Visibility.Collapsed);
            var view = View()!;
            var text = await view.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            Assert(text.Length > 30, "Empty Office document: " + path);
            Assert(!view.CoreWebView2.Settings.IsScriptEnabled, "Document scripts enabled");
            await view.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0,document.body.scrollHeight)");
            await Task.Delay(150);
            await view.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0,0)");
            await Task.Delay(300);
            var point = view.PointToScreen(new Point(5, 5));
            var corner = view.PointToScreen(new Point(view.ActualWidth - 5, view.ActualHeight - 5));
            Assert(InputProbe.OwnsRootPoint(new Point((point.X + corner.X) / 2, (point.Y + corner.Y) / 2)), "Another window covers the office preview");
            using var image = new System.Drawing.Bitmap((int)(corner.X - point.X), (int)(corner.Y - point.Y));
            using (var graphics = System.Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen((int)point.X, (int)point.Y, 0, 0, image.Size);
            image.Save(Path.Combine(profile, Path.GetFileName(path) + ".png"));
            File.WriteAllText(Path.Combine(profile, Path.GetFileName(path) + ".text.json"), text);
        }
        Call("ClearPreview");
        ((TextBox)window.FindName("QueryBox")).Text = "fixture";
        var results = (ListBox)window.FindName("Results");
        await Wait(() => results.Items.Count > 2 && !(bool)window.GetType().GetField("_pending", flags)!.GetValue(window)!);
        results.SelectedIndex = 1;
        await Wait(() => ((Popup)window.FindName("PreviewPopup")).IsOpen);
        Call("OpenResultMenu", true);
        Assert(!((Popup)window.FindName("PreviewPopup")).IsOpen && View() is null, "Keyboard menu retained preview");
        var menu = (ContextMenu)window.GetType().GetField("_resultMenu", flags)!.GetValue(window)!;
        menu.IsOpen = false;
        var item = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(2);
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        await Task.Delay(400);
        Assert(results.SelectedIndex == 2 && !((Popup)window.FindName("PreviewPopup")).IsOpen, "Right selection opened preview");
        results.SelectedIndex = 1;
        await Wait(() => ((Popup)window.FindName("PreviewPopup")).IsOpen);
        Call("Dismiss");
        File.WriteAllText(Path.Combine(profile, "office-result.json"), "{\"Passed\":true,\"Formats\":7,\"ContentRendered\":true,\"ScreenCaptured\":true,\"RightSelectionSuppressesPreview\":true,\"KeyboardMenuClosesPreview\":true,\"SelectionResumesPreview\":true}");
    }
}
