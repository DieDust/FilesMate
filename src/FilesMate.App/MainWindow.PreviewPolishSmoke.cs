#if FILESMATE_UI_TEST
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Models;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunPreviewPolishSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        QuickPreviewWindow? active = null;
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } page && !page.ViewModel.IsLoading, "navigator");
            AppWindow.Resize(new(1800, 1100));
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "preview-polish-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            File.WriteAllLines(Path.Combine(fixture, "reading.txt"), Enumerable.Range(1, 200).Select(i => $"第 {i} 行 · 预览内容随文件选择更新"));
            File.WriteAllText(Path.Combine(fixture, "notes.txt"), "另一份文件\n保留预览窗口并切换内容");
            AddNavigatorTab(fixture);
            await Wait(() => TabHost.Content is NavigatorPage page && page.ViewModel.AddressText == fixture && !page.ViewModel.IsLoading && page.ViewModel.ItemCount == 2, "fixture");
            var navigator = (NavigatorPage)TabHost.Content;
            await Wait(() => ((System.Collections.IDictionary)typeof(NavigatorPage).GetField("_restoringViews", flags)!.GetValue(navigator)!).Count == 0, "folder view restore");
            var surface = (FileDetailsSurface)navigator.FindName("FileSurface");
            var toggle = (EventHandler)typeof(FileDetailsSurface).GetField("QuickPreviewRequested", flags)!.GetValue(surface)!;
            QuickPreviewWindow? Card() => (QuickPreviewWindow?)typeof(NavigatorPage).GetField("_quickPreview", flags)!.GetValue(navigator);
            async Task<QuickPreviewWindow> Open()
            {
                Activate(); Foreground(NativeHandle); surface.TrySelectByName("reading.txt"); surface.Focus(FocusState.Programmatic);
                await Task.Delay(120); toggle(surface, EventArgs.Empty);
                await Wait(() => Card() is { Content: FrameworkElement { IsLoaded: true } }, "preview opened");
                active = Card()!; Foreground(WinRT.Interop.WindowNative.GetWindowHandle(active));
                await active.LoadAsync(Path.Combine(fixture, "reading.txt")); await Task.Delay(200); return active;
            }
            await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Light);
            var card = await Open();
            var root = (FrameworkElement)card.Content;
            var pane = PolishDescendants(root).OfType<PreviewPane>().Single();
            var viewport = pane.PreviewViewport;
            var top = viewport.TransformToVisual(root).TransformPoint(default).Y;
            Require(top <= 88 && viewport.ActualHeight > root.ActualHeight * .75, "preview header consumes too much space");
            report["PreviewContentTop"] = top;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(card);
            Require(Hit(hwnd, root, new(root.ActualWidth - 40, top - 10)) == 2, "blank header must be native caption");
            var tab = pane.HeaderControls.First();
            var tabCenter = tab.TransformToVisual(root).TransformPoint(new(tab.ActualWidth / 2, tab.ActualHeight / 2));
            report["HeaderHit"] = new { Blank = Hit(hwnd, root, new(root.ActualWidth - 40, top - 10)), Tab = Hit(hwnd, root, tabCenter), tabCenter.X, tabCenter.Y };
            Require(Hit(hwnd, root, tabCenter) != 2, "preview tab swallowed by caption");
            await Capture(root, "quick-preview-light.png");
            report["HeaderDragAndButtonPassthrough"] = true;
            navigator.DismissQuickPreviewOutside(PolishDescendants(surface).OfType<FileRow>().First(row => row.EntryId >= 0));
            Require(ReferenceEquals(card, Card()), "file click dismissed preview before selection");
            surface.TrySelectByName("notes.txt");
            await Wait(() => card.Title == "notes.txt", "selection follows");
            Require(ReferenceEquals(card, Card()), "selection replaced preview window");
            report["SelectionReusesPreview"] = true;
            navigator.DismissQuickPreviewOutside(surface);
            await Wait(() => Card() is null, "blank area dismiss");
            Require(!card.RestoreOwnerFocus, "dismiss steals keyboard focus");
            await Task.Delay(180);
            card = await Open();
            var omni = (DependencyObject)navigator.FindName("Omni");
            var input = PolishDescendants(omni).OfType<TextBox>().First(t => t.IsLoaded && t.ActualWidth > 0);
            Activate(); input.Focus(FocusState.Keyboard);
            await Wait(() => Card() is null, "keyboard focus outside file selection");
            Require(!card.RestoreOwnerFocus, "outside focus restored to list");
            report["OutsidePointerAndKeyboardDismiss"] = true;
            await Task.Delay(180);
            card = await Open();
            surface.Selection.Clear();
            ((EventHandler)typeof(FileDetailsSurface).GetField("SelectionChanged", flags)!.GetValue(surface)!)(surface, EventArgs.Empty);
            await Wait(() => Card() is null, "selection cleared");
            report["ClearingSelectionDismisses"] = true;
            await Task.Delay(180);
            card = await Open();
            var otherWindow = new Window { Content = new Grid() };
            try
            {
                otherWindow.Activate(); Foreground(WinRT.Interop.WindowNative.GetWindowHandle(otherWindow));
                await Wait(() => Card() is null, "unrelated foreground window");
                Require(!card.RestoreOwnerFocus, "external dismissal restores focus");
            }
            finally { otherWindow.Close(); }
            report["ExternalForegroundDismisses"] = true;
            await Task.Delay(180);
            card = await Open();
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            ((FrameworkElement)card.Content).RequestedTheme = ElementTheme.Dark;
            await Task.Delay(200); await Capture(card.Content, "quick-preview-dark.png");
            OpenSettings("files-folders");
            await Wait(() => _settingsPage?.IsLoaded == true, "settings");
            await Wait(() => Card() is null, "settings dismiss");
            var settings = _settingsPage!;
            var search = (AutoSuggestBox)settings.FindName("SettingsSearchBox");
            var categories = (ListView)settings.FindName("CategoryList");
            var searchBounds = search.TransformToVisual(settings).TransformBounds(new(0, 0, search.ActualWidth, search.ActualHeight));
            var categoryBounds = categories.TransformToVisual(settings).TransformBounds(new(0, 0, categories.ActualWidth, categories.ActualHeight));
            Require(searchBounds.Right < 242 && searchBounds.Bottom <= categoryBounds.Top + 1, "settings search not above categories");
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme); await Task.Delay(180);
                var palette = SkinPalette.For(theme == AppThemeKind.Dark);
                var ink = (SolidColorBrush)Theming.ThemeResources.Resolve(SettingsPanel, "FilesMate.Text.PrimaryBrush")!;
                Require(ink.Color == Themes.ShellStyleResources.FromArgb(palette.Text), "settings skin text did not switch with its surface");
                await Capture(SettingsPanel, $"settings-search-{theme}.png");
            }
            CloseSettings();
            var status = (FrameworkElement)navigator.FindName("SharedStatusBar");
            var paneCard = (FrameworkElement)((FilePaneChrome)navigator.FindName("PaneChrome")).FindName("PaneCard");
            var scroller = (ScrollViewer)surface.FindName("Scroller");
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
            scroller.UpdateLayout(); await Task.Delay(100);
            var horizontalBar = PolishDescendants(scroller).OfType<Microsoft.UI.Xaml.Controls.Primitives.ScrollBar>().Single(bar => bar.Orientation == Orientation.Horizontal);
            report["FooterMetrics"] = new { status.ActualHeight, PaneBottomMargin = paneCard.Margin.Bottom, ScrollBarHeight = horizontalBar.ActualHeight };
            Require(status.ActualHeight <= 28.1 && paneCard.Margin.Bottom == 0 && horizontalBar.ActualHeight <= 12.1, "status gap remains oversized");
            await Capture(status, "compact-status.png");
            await Capture((FrameworkElement)navigator.FindName("PaneChrome"), "compact-file-pane.png");
            report["SettingsSearchInSidebarAndCompactFooter"] = true;
            report["Passed"] = true;
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        finally { active?.Dismiss(); await Task.Delay(300); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-polish-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        static async Task Wait(Func<bool> condition, string name)
        { for (var i = 0; i < 150; i++) { if (condition()) return; await Task.Delay(60); } throw new TimeoutException(name); }
        static long Hit(nint window, FrameworkElement root, Point point)
        {
            var screen = new PreviewPolishPoint(); PreviewPolishClientToScreen(window, ref screen);
            var x = screen.X + (int)Math.Round(point.X * root.XamlRoot.RasterizationScale);
            var y = screen.Y + (int)Math.Round(point.Y * root.XamlRoot.RasterizationScale);
            return (long)PreviewPolishSendMessage(window, 0x84, 0, (nint)((y << 16) | (x & 0xffff)));
        }
        static void Foreground(nint window)
        {
            var current = BrowsingGetCurrentThreadId();
            var foreground = BrowsingGetWindowThreadProcessId(BrowsingGetForegroundWindow(), out _);
            var attached = current != foreground && BrowsingAttachThreadInput(current, foreground, true);
            try { BrowsingSetForegroundWindow(window); }
            finally { if (attached) BrowsingAttachThreadInput(current, foreground, false); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct PreviewPolishPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")] private static extern bool PreviewPolishClientToScreen(nint window, ref PreviewPolishPoint point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint PreviewPolishSendMessage(nint window, uint message, nuint wparam, nint lparam);
}
#endif
