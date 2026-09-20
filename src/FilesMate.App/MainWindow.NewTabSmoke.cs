#if FILESMATE_UI_TEST
using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using FilesMate.App.Navigation;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunNewTabSmokeAsync()
    {
        if (Environment.GetEnvironmentVariable("FILESMATE_CREATE_RENAME_SMOKE") == "1")
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 1050));
            await Task.Delay(1200);
            if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { Navigator: { } page } })
                await page.RunCreateRenameSmokeAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_MARQUEE_SMOKE") == "1")
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 1000));
            var surface = new Controls.FileSurface.FileDetailsSurface { Width = 800, Height = 500,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var host = (Grid)Content;
            host.Children.Add(surface);
            await Task.Delay(1000);
            await surface.RunMarqueeSmokeAsync();
            host.Children.Remove(surface);
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_CORNERS_CAPTURE") == "1")
        {
            await RunCornersSmokeAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_CONTEXT_MENU_CAPTURE") == "1")
        {
            await RunContextMenuSmokeAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_RENAME_TAG_CAPTURE") == "1")
        {
            await RunRenameTagSmokeAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_SCROLLBAR_CAPTURE") == "1")
        {
            await RunScrollbarSmokeAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_SHELL_HIERARCHY_CAPTURE") == "1")
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            await Task.Delay(1600);
            var measurements = new List<object>();
            foreach (var theme in new[] { Models.AppThemeKind.Light, Models.AppThemeKind.Dark })
            {
                await App.AppearanceViewModel!.SetThemeAsync(theme);
                foreach (var style in new[] { Models.ShellStyleKind.Layered, Models.ShellStyleKind.Unified, Models.ShellStyleKind.Layered })
                {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1000));
                    OpenSettings("appearance");
                    await Task.Delay(600);
                    var box = FindDescendant<ComboBox>((DependencyObject)Content, b => b.Name == "ShellStyleBox")!;
                    box.SelectedItem = box.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == style.ToString());
                    await Task.Delay(600);
                    if (App.AppearanceViewModel.Current.ShellStyle != style) throw new InvalidOperationException("Style selector did not apply.");
                    await Capture((UIElement)Content, $"shell-settings-{theme}-{style}.png");
                    CloseSettings();
                    foreach (var size in new[] { new Windows.Graphics.SizeInt32(1600, 1000), new Windows.Graphics.SizeInt32(1080, 760) })
                    {
                        AppWindow.Resize(size);
                        await Task.Delay(600);
                        await Capture((UIElement)Content, $"shell-hierarchy-{theme}-{style}-{size.Width}.png");
                        var footer = FindDescendant<Grid>((DependencyObject)Content, g => g.Name == "SettingsFooter")!;
                        var button = FindDescendant<Button>(footer, b => b.Name == "SettingsButton")!;
                        var bounds = button.TransformToVisual(footer).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                        var bottom = footer.ActualHeight - bounds.Bottom;
                        if (Math.Abs(bounds.Top - bottom) > 0.1) throw new InvalidOperationException("Settings footer spacing is asymmetric.");
                        measurements.Add(new { theme, style, size.Width, Top = bounds.Top, Bottom = bottom });
                    }
                }
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "shell-hierarchy-capture.done"), JsonSerializer.Serialize(measurements));
            return;
        }
        var samples = new List<object>();
        var failures = new List<string>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            await Task.Delay(1200);
            for (var i = 0; i < 12; i++)
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(new[] { 900, 1200, 1600 }[i % 3], 850));
                if (Tabs.SelectedItem is TabViewItem current)
                    ApplyHeader(current, i % 2 == 0 ? "A" : "A much longer folder name for hit testing");
                await Task.Delay(80);
                var scale = NewTabButton.XamlRoot.RasterizationScale;
                var bounds = NewTabButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, NewTabButton.ActualWidth, NewTabButton.ActualHeight));
                var regions = InputNonClientPointerSource.GetForWindowId(AppWindow.Id).GetRegionRects(NonClientRegionKind.Passthrough);
                var covered = new[] { 2d, bounds.Width / 2, bounds.Width - 2 }.All(x =>
                    regions.Any(r => (bounds.X + x) * scale >= r.X && (bounds.X + x) * scale < r.X + r.Width
                        && (bounds.Y + bounds.Height / 2) * scale >= r.Y && (bounds.Y + bounds.Height / 2) * scale < r.Y + r.Height));
                if (!covered) failures.Add($"Click region stale at step {i}");
                var count = Tabs.TabItems.Count;
                ((IInvokeProvider)new ButtonAutomationPeer(NewTabButton).GetPattern(PatternInterface.Invoke)).Invoke();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 3000 && Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent tab }
                    && (tab.Navigator is null || !tab.Navigator.IsLoaded || tab.Navigator.ViewModel.AddressText != HomeLocation.Uri))
                    await Task.Delay(20);
                if (Tabs.TabItems.Count != count + 1 || Tabs.SelectedItem is not TabViewItem { Tag: NavigatorTabContent { Navigator: { } page } }
                    || page.ViewModel.AddressText != HomeLocation.Uri) failures.Add($"Single invocation did not open Home at step {i}");
                samples.Add(new { Step = i, Covered = covered, HomeMilliseconds = timer.ElapsedMilliseconds, Tabs = Tabs.TabItems.Count });
                if (i % 3 == 2) CloseTab((TabViewItem)Tabs.SelectedItem);
            }
            await HomePlaces.LoadDrivesAsync();
            using var release = new ManualResetEventSlim();
            HomePlaces.TestDriveReader = () => { release.Wait(TimeSpan.FromSeconds(10)); return []; };
            var pendingDrive = HomePlaces.LoadDrivesAsync();
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                ((IInvokeProvider)new ButtonAutomationPeer(NewTabButton).GetPattern(PatternInterface.Invoke)).Invoke();
                while (timer.ElapsedMilliseconds < 2000 && Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent tab }
                    && (tab.Navigator is null || !tab.Navigator.IsLoaded || tab.Navigator.ViewModel.AddressText != HomeLocation.Uri))
                    await Task.Delay(20);
                var shown = Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { Navigator: { IsLoaded: true } home } }
                    && home.ViewModel.AddressText == HomeLocation.Uri;
                if (!shown || pendingDrive.IsCompleted) failures.Add("Home did not render independently of a stalled drive query");
                samples.Add(new { SlowDrive = true, HomeVisible = shown, HomeMilliseconds = timer.ElapsedMilliseconds, DiskStillPending = !pendingDrive.IsCompleted });
            }
            finally { release.Set(); await pendingDrive; HomePlaces.TestDriveReader = null; }
            if (Tabs.SelectedItem is TabViewItem finalTab)
            {
                foreach (var extra in Tabs.TabItems.OfType<TabViewItem>().Where(t => !ReferenceEquals(t, finalTab)).ToArray()) CloseTab(extra);
                AddHomeTab();
                await Task.Delay(500);
                finalTab = (TabViewItem)Tabs.SelectedItem;
                var close = FindDescendant<Button>(finalTab, b => b.Name == "CloseButton");
                if (close is not null) VisualStateManager.GoToState(close, "PointerOver", false);
                await Task.Delay(100);
                await Capture(AppTitleBar, "tab-close-hover.png");
                if (close is not null)
                {
                    var paint = FindDescendant<ContentPresenter>(close, _ => true);
                    samples.Add(new { CloseHitWidth = close.ActualWidth, CloseHitHeight = close.ActualHeight,
                        ClosePaintWidth = paint?.ActualWidth, ClosePaintHeight = paint?.ActualHeight });
                    await Capture(close, "tab-close-hover-detail.png");
                }
                if (TabHost.Content is UIElement page) await Capture(page, "sidebar-spacing.png");
            }
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "new-tab-smoke.json"), JsonSerializer.Serialize(new { Passed = failures.Count == 0, Failures = failures, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task Capture(UIElement element, string name)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        using var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels);
        var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        using var file = File.Open(Path.Combine(AppContext.BaseDirectory, name), FileMode.Create);
        using var stream = file.AsRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
}
#endif
