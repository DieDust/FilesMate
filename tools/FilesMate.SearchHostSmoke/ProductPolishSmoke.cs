using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FilesMate.App.Models;
using FilesMate.Search;
using FilesMate.SearchHost;

internal static class ProductPolishSmoke
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Field(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args)!;
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }

    internal static int Run(string output, bool previewOnly = false)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await Verify(output, previewOnly); app.Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static async Task Verify(string output, bool previewOnly)
    {
        var assembly = typeof(PaletteWindow).Assembly;
        var host = RuntimeHelpers.GetUninitializedObject(assembly.GetType("FilesMate.SearchHost.SearchHost")!);
        Set(host, "<Profile>k__BackingField", output);
        Set(host, "<Settings>k__BackingField", new GlobalSearchSettings(StartAtLogin: false, PreviewEnabled: false));
        Set(host, "_app", Application.Current);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var warmClock = System.Diagnostics.Stopwatch.StartNew();
        var window = (PaletteWindow)Activator.CreateInstance(typeof(PaletteWindow), Flags, null, [host, new Provider(output)], null)!;
        // Off-screen, inactive window: exercise real WPF layout without mouse,
        // hotkeys, tray registration, or a connection to the user's profile.
        var area = RuntimeHelpers.GetUninitializedObject(typeof(PaletteWindow).GetField("_workArea", Flags)!.FieldType);
        area.GetType().GetField("Left")!.SetValue(area, -10000);
        area.GetType().GetField("Right")!.SetValue(area, -8000);
        area.GetType().GetField("Bottom")!.SetValue(area, 1200);
        Set(window, "_workArea", area);
        Set(window, "_opening", true);
        window.ShowActivated = false;
        window.Left = window.Top = -10000;
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (previewOnly)
        {
            var previewEditor = (ICSharpCode.AvalonEdit.TextEditor)typeof(PaletteWindow).GetProperty("PreviewText", Flags)!.GetValue(window)!;
            await VerifyPreviewScrollbars(window, previewEditor, output, assembly.GetType("FilesMate.SearchHost.PaletteAppearance")!);
            window.Close();
            File.WriteAllText(Path.Combine(output, "result.json"), "{\"Passed\":true,\"Themes\":2,\"Scales\":3,\"HorizontalDrag\":true,\"ThemedCorner\":true,\"ShortText\":true}");
            return;
        }
        var warmAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var warmMilliseconds = warmClock.Elapsed.TotalMilliseconds;
        Call(window, "ShowSettings", true, "");
        Call(window, "Ranking_Click", new object(), new RoutedEventArgs());
        window.UpdateLayout();
        var ranking = (ItemsControl)Field(window, "RankingItems");
        var rankCards = Visuals(ranking).OfType<Border>().Where(b => b.Child is Grid grid
            && grid.Children.OfType<Border>().Any(child => child.Name == "RankDropIndicator")).ToArray();
        foreach (var row in rankCards)
        {
            var title = Visuals(row).OfType<TextBlock>().First();
            var position = title.TranslatePoint(new Point(), window);
            var height = row.ActualHeight;
            var border = row.BorderThickness;
            var brush = row.BorderBrush;
            foreach (var after in new[] { false, true })
            {
                Call(window, "ShowRankDropIndicator", row, after);
                window.UpdateLayout();
                Check(title.TranslatePoint(new Point(), window) == position && row.ActualHeight == height
                    && row.BorderThickness == border && row.BorderBrush == brush, "Rank drag feedback moved content or changed the card border");
            }
            Call(window, "ClearRankDropIndicator");
            window.UpdateLayout();
            Check(title.TranslatePoint(new Point(), window) == position, "Rank drag exit moved the title");
        }
        Check(rankCards.Length > 0, "Rank geometry test did not realize cards");
        Call(window, "ShowSettings", false, "");
        Check(Field(window, "_previewText") is null, "Unused text preview was constructed during startup");
        ((TextBox)Field(window, "QueryBox")).Text = "fixture";
        await Task.Delay(900);
        var results = (ListBox)Field(window, "Results");
        var rows = results.Items.Cast<SearchRow>().ToArray();
        var appRow = new SearchRow(new NameHit("app", "test-app", false, new ApplicationEntry("app", "App", "unused")), new DrawingImage());
        var gesture = typeof(PaletteWindow).GetMethod("ShouldLaunchApplicationClick", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Click(object released, MouseButton button, ModifierKeys modifiers, Vector movement)
            => (bool)gesture.Invoke(null, [appRow, released, button, modifiers, movement])!;
        Check(Click(appRow, MouseButton.Left, ModifierKeys.None, new Vector()), "Application click does not activate");
        foreach (var modifier in new[] { ModifierKeys.Control, ModifierKeys.Shift, ModifierKeys.Alt })
            Check(!Click(appRow, MouseButton.Left, modifier, new Vector()), "Modifier selection would launch application");
        Check(!Click(rows[0], MouseButton.Left, ModifierKeys.None, new Vector())
            && !Click(appRow, MouseButton.Right, ModifierKeys.None, new Vector())
            && !Click(appRow, MouseButton.Left, ModifierKeys.None, new Vector(100, 0)), "Different row, context click or drag would launch application");
        bool Loaded(SearchRow row) => (bool)typeof(SearchRow).GetProperty("IconLoaded", Flags)!.GetValue(row)!;
        var initialLoaded = rows.Count(Loaded);
        Check(rows.Length == 40 && initialLoaded > 0 && initialLoaded < 12, $"Unexpected viewport: rows={rows.Length}, loaded={initialLoaded}, height={results.ActualHeight}, pending={Field(window, "_pending")}");
        results.ScrollIntoView(rows[^1]);
        await Task.Delay(250);
        Check(Loaded(rows[^1]) && !Loaded(rows[20]) && !Loaded(rows[0]) && rows.Count(Loaded) < 12, "Scrolling did not populate only the new viewport");
        Call(window, "CancelSearch", false);
        Check(!((DispatcherTimer)Field(window, "_visibleIconsTimer")).IsEnabled && ((IDictionary)Field(window, "_visibleIconLoads")).Count == 0,
            "Canceled search retained scheduled icon work");

        var icons = Field(window, "_icons");
        var image = new DrawingImage();
        image.Freeze();
        for (var i = 0; i < 128; i++) Call(icons, "Cache", "icon" + i, image);
        var cache = (IDictionary)Field(icons, "_shellImages");
        Call(icons, "TryCached", "icon0", null!);
        Call(icons, "Cache", "icon128", image);
        Check(cache.Count == 128 && cache.Contains("icon0") && !cache.Contains("icon1") && cache.Contains("icon128"), "Cache flushed warm entries instead of evicting least recently used");
        Call(icons, "Cache", "icon0", image);
        Check(cache.Count == 128, "Updating an existing cache entry evicted another icon");
        var stale = cache["icon2"]!;
        cache["icon2"] = Activator.CreateInstance(stale.GetType(), image, Environment.TickCount64 - 61000, 0L);
        Call(icons, "ClearDynamicCache");
        Check(cache.Count == 127 && !cache.Contains("icon2") && cache.Contains("icon0"), "Expiry cleared fresh icons or kept stale entries");

        var appearance = assembly.GetType("FilesMate.SearchHost.PaletteAppearance")!;
        foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
        {
            appearance.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [window.Resources, AppearanceSettings.Default with { Theme = theme }]);
            Check(((SolidColorBrush)window.Resources["CloseHover"]).Color == (SystemParameters.HighContrast ? SystemColors.HighlightColor : Color.FromRgb(232, 17, 35)), "Close feedback drifted from main theme");
            window.UpdateLayout();
            var surface = (Border)Field(window, "Shell");
            var shot = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            shot.Render(surface);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(shot));
            using var stream = File.Create(Path.Combine(output, theme + ".png"));
            png.Save(stream);
        }
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        Check((bool)typeof(PaletteWindow).GetProperty("AnimateResultLayout", Flags)!.GetValue(window)! == SystemParameters.ClientAreaAnimation, "Software rendering silently disabled requested motion");
        Call(window, "AnimateSearchLayout");
        await Task.Delay(300);
        Call(window, "ResetPaletteMotion");
        Call(window, "PreparePaletteOpen");
        if (SystemParameters.ClientAreaAnimation)
            Check(((Border)Field(window, "Shell")).Opacity == 0, "First frame is visible before entrance begins");
        var motionFrames = new System.Collections.Generic.List<double>();
        EventHandler frame = (_, _) => motionFrames.Add(((ScaleTransform)Field(window, "_paletteScale")).ScaleX);
        CompositionTarget.Rendering += frame;
        var visibleCapture = Environment.GetEnvironmentVariable("FILESMATE_VISIBLE_MOTION_TEST") == "1";
        if (visibleCapture) SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), 1400, 300, 0, 0, 0x11);
        try
        {
            Call(window, "AnimatePaletteOpen");
            await Task.Delay(50);
            CaptureMotionFrame(window, Path.Combine(output, "Entrance50.png"));
            await Task.Delay(80);
            CaptureMotionFrame(window, Path.Combine(output, "Entrance130.png"));
            await Task.Delay(190);
            CaptureMotionFrame(window, Path.Combine(output, "EntranceSettled.png"));
        }
        finally
        {
            CompositionTarget.Rendering -= frame;
            if (visibleCapture) SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, -10000, -10000, 0, 0, 0x15);
        }
        if (SystemParameters.ClientAreaAnimation)
            Check(motionFrames.Count(value => value > .98 && value < 1) >= 3, "Entrance has no intermediate rendered frames");
        Check(((ScaleTransform)Field(window, "_paletteScale")).ScaleX == 1, "Entrance did not settle");
        Check(((FrameworkElement)Field(window, "SearchPanel")).ActualHeight > 300, "Software rendering clipped results");
        Set(window, "_appearance", AppearanceSettings.Default with { ReduceMotion = ReduceMotionKind.On });
        Call(window, "ResetPaletteMotion");
        Call(window, "AnimatePaletteOpen");
        Check(!((TranslateTransform)Field(window, "_paletteTranslation")).HasAnimatedProperties, "Reduced motion animated the palette");
        var editorProperty = typeof(PaletteWindow).GetProperty("PreviewText", Flags)!;
        var editor = (ICSharpCode.AvalonEdit.TextEditor)editorProperty.GetValue(window)!;
        await VerifyPreviewScrollbars(window, editor, output, appearance);
        editor.Text = "preview fixture";
        Check(editor.Document.UndoStack.SizeLimit == 0 && editor.IsReadOnly, "Preview editor keeps unnecessary edit history");
        Call(window, "ClearPreview");
        Check(editor.Text.Length == 0, "Closed preview retained document text");
        Call(window, "Dismiss", false);
        Check(Field(window, "_previewText") is null && ((ContentControl)Field(window, "PreviewTextHost")).Content is null, "Dismissed palette retains preview editor");
        window.Close();
        File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Passed = true, WarmAllocatedBytes = warmAllocatedBytes, WarmMilliseconds = warmMilliseconds, Results = rows.Length, InitialIconsLoaded = initialLoaded,
            ScrollLoadsVisibleOnly = true, Cancellation = true, CacheLruAndExpiry = true, RankDragGeometry = true,
            ThemeConsistency = true, PreviewScrollbars = true, SoftwareRendering = true, ReducedMotion = true, LazyTextPreview = true, ApplicationClickSafety = true, EntranceIntermediateFrames = motionFrames.Count(value => value > .98 && value < 1),
        }));
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static async Task VerifyPreviewScrollbars(PaletteWindow palette, ICSharpCode.AvalonEdit.TextEditor editor, string output, Type appearance)
    {
        var host = (ContentControl)Field(palette, "PreviewTextHost");
        host.Content = null;
        var preview = new Window { Left = -10000, Top = -10000, Width = 440, Height = 380,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = editor };
        preview.Resources.MergedDictionaries.Add(palette.Resources);
        preview.SetResourceReference(Control.BackgroundProperty, "Surface");
        editor.Visibility = Visibility.Visible;
        editor.Text = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"{i:D3} long preview line " + new string('x', 240)));
        try
        {
            preview.Show();
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            foreach (var scale in new[] { 1d, 1.5, 2 })
            {
                appearance.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [palette.Resources, AppearanceSettings.Default with { Theme = theme }]);
                editor.LayoutTransform = new ScaleTransform(scale, scale);
                preview.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.Render);
                var scroll = Visuals(editor).OfType<ScrollViewer>().First();
                var horizontal = Visuals(scroll).OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Horizontal);
                var vertical = Visuals(scroll).OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
                var track = (Track)horizontal.Template.FindName("PART_Track", horizontal);
                Check(horizontal.IsVisible && vertical.IsVisible && horizontal.ActualWidth > 100 && horizontal.ActualHeight <= 6,
                    $"{theme}/{scale}: horizontal scrollbar geometry is {horizontal.ActualWidth}x{horizontal.ActualHeight}");
                Check(track.Orientation == Orientation.Horizontal && !track.IsDirectionReversed && track.Thumb.ActualWidth > track.Thumb.ActualHeight,
                    "Horizontal thumb uses a vertical template");
                scroll.ScrollToHorizontalOffset(0); scroll.ScrollToVerticalOffset(0); preview.UpdateLayout();
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(24, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                preview.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.Render);
                Check(scroll.HorizontalOffset > 0 && scroll.VerticalOffset == 0, "Horizontal drag failed or moved vertically");
                var shot = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                shot.Render(preview);
                var corner = scroll.TranslatePoint(new Point(scroll.ActualWidth - 2, scroll.ActualHeight - 2), preview);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(shot));
                using (var file = File.Create(Path.Combine(output, $"preview-scroll-{theme}-{scale}.png"))) png.Save(file);
                var pixel = new byte[4]; shot.CopyPixels(new Int32Rect((int)corner.X, (int)corner.Y, 1, 1), pixel, 4, 0);
                var background = ((SolidColorBrush)palette.Resources["Surface"]).Color;
                Check(Math.Abs(pixel[0] - background.B) < 3 && Math.Abs(pixel[1] - background.G) < 3 && Math.Abs(pixel[2] - background.R) < 3,
                    $"{theme}/{scale}: scroll corner differs from themed background: pixel={string.Join(',', pixel)}, expected={background}, position={corner}");
            }
            editor.Text = "short"; preview.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.Render);
            Check(!Visuals(editor).OfType<ScrollBar>().Any(b => b.IsVisible), "Short text retains unnecessary scrollbars");
        }
        finally { preview.Content = null; preview.Close(); editor.LayoutTransform = Transform.Identity; host.Content = editor; }
    }

    private static void CaptureMotionFrame(Window window, string path)
    {
        if (Environment.GetEnvironmentVariable("FILESMATE_VISIBLE_MOTION_TEST") != "1")
        {
            var shot = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            shot.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(shot));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return;
        }
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Test window disappeared");
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            GetWindowRgn(handle, region);
            GetRgnBox(region, out var clip);
            if (Path.GetFileName(path) == "EntranceSettled.png")
                Check(Math.Abs(clip.Right - (bounds.Right - bounds.Left)) <= 1 && Math.Abs(clip.Bottom - (bounds.Bottom - bounds.Top)) <= 1,
                    "Native region still clips the expanded result area");
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "native-geometry.txt"), $"{Path.GetFileName(path)}: hwnd={bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}, clip={clip.Right}x{clip.Bottom}, logical={window.ActualWidth}x{window.ActualHeight}\n");
        }
        finally { DeleteObject(region); }
        using var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region, out NativeRect bounds);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr region);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);

    private sealed class Provider(string output) : IGlobalSearchProvider
    {
        public Task<GlobalSearchResponse> SearchAsync(string query, CancellationToken token, SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0)
            => Task.FromResult(new GlobalSearchResponse(Enumerable.Range(0, 40)
                .Select(i => new NameHit($"fixture{i}.txt", Path.Combine(output, $"fixture{i}.txt"), false)).ToArray(), false));
    }
}
