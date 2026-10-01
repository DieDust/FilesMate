#if FILESMATE_UI_TEST
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Models;
using FilesMate.App.Views;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyMenuPointerAsync(Dictionary<string, object> report)
    {
        var original = TabHost.Content;
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var foreground = MenuPointerGetForegroundWindow();
        MenuPointerGetCursorPos(out var cursor);
        var checks = new List<object>();
        var events = new List<object>();
        report["RealMenuPointer"] = checks;
        report["RealMenuPointerEvents"] = events;
        var originalTab = Tabs.SelectedItem;
        TabViewItem? fixtureTab = null;
        var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        var wasOnTop = presenter?.IsAlwaysOnTop ?? false;
        try
        {
            AppWindow.Move(new(100, 100)); AppWindow.Resize(new(1800, 1250));
            if (presenter is not null) presenter.IsAlwaysOnTop = true;
            // The compatibility HWND can have no OverlappedPresenter. Make this
            // isolated fixture visible and topmost before testing desktop input.
            MenuPointerSetWindowPos(NativeHandle, -1, 0, 0, 0, 0, 0x0043);
            Activate();
            MenuPointerSetForegroundWindow(NativeHandle);
            var folder = Path.Combine(AppContext.BaseDirectory, "menu-pointer-fixture");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "menu-test.txt"), "menu input fixture");
            AddNavigatorTab(folder);
            fixtureTab = (TabViewItem)Tabs.SelectedItem;
            for (var attempt = 0; attempt < 160 && (TabHost.Content is not NavigatorPage { IsLoaded: true } ready || ready.ViewModel.IsLoading || ready.ViewModel.AddressText != folder); attempt++)
                await Task.Delay(25);
            var page = (NavigatorPage)TabHost.Content;
            if (!page.IsLoaded || page.ViewModel.IsLoading || page.ViewModel.AddressText != folder) throw new TimeoutException("Real navigator did not load");
            var toolbar = (AdaptiveCommandToolbar)page.FindName("Commands");
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            surface.SetLayout(FileLayoutKind.Details);
            toolbar.SetLayout(FileLayoutKind.Details);
            await Task.Delay(250);
            var newButton = (Button)toolbar.FindName("NewButton");
            var commandRow = (FrameworkElement)page.FindName("CommandBarRow");
            var inset = newButton.TransformToVisual(commandRow).TransformPoint(new()).X;
            if (inset < 4 || newButton.ActualWidth < 40) throw new InvalidOperationException("New command is cramped against the pane edge");
            report["RealToolbarInset"] = new { Inset = inset, Width = newButton.ActualWidth };
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel!.SetThemeAsync(theme);
                var button = (Button)toolbar.FindName("ViewMenuButton");
                var view = (MenuFlyout)button.Flyout;
                view.ShowAt(button); await Task.Delay(180);
                var alignment = view.Items.Where(i => i is MenuFlyoutItem or MenuFlyoutSubItem).Select(item =>
                {
                    var text = PolishDescendants(item).OfType<TextBlock>().Single(t => t.Name == "TextBlock");
                    return new { Text = item is MenuFlyoutItem leaf ? leaf.Text : ((MenuFlyoutSubItem)item).Text,
                        Left = text.TransformToVisual(item).TransformPoint(new()).X, Height = item.ActualHeight };
                }).ToArray();
                report["ViewMenuAlignment-" + theme] = alignment;
                if (alignment.Any(i => Math.Abs(i.Left - 28) > .7 || Math.Abs(i.Height - 32) > .7))
                    throw new InvalidOperationException("View choices and submenus do not share compact aligned labels");
                try { await Probe(view, "View-" + theme); }
                finally { view.Hide(); }
                if (Environment.GetEnvironmentVariable("FILESMATE_PHYSICAL_POINTER_SMOKE") == "1")
                {
                    view.ShowAt(button); await Task.Delay(100);
                    try { await ProbeSubmenu(view.Items.OfType<MenuFlyoutSubItem>().Last(), "ViewZoom-" + theme); }
                    finally { view.Hide(); }
                }
                surface.SetLayout(FileLayoutKind.Details);
                toolbar.SetLayout(FileLayoutKind.Details);
                await Task.Delay(100);
                typeof(FileDetailsSurface).GetMethod("ShowColumnMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [new Point(60, 20)]);
                await Task.Delay(180);
                var columns = (MenuFlyout)typeof(FileDetailsSurface).GetField("_columnMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
                try { await Probe(columns, "Columns-" + theme); }
                finally { columns.Hide(); }
                await Task.Delay(100);
            }
            report["RealMenuPointerPassed"] = true;
        }
        finally
        {
            TabHost.Content = original;
            Tabs.SelectedItem = originalTab;
            if (fixtureTab is not null) Tabs.TabItems.Remove(fixtureTab);
            AppWindow.MoveAndResize(new(position.X, position.Y, size.Width, size.Height));
            if (presenter is not null) presenter.IsAlwaysOnTop = wasOnTop;
            if (!wasOnTop) MenuPointerSetWindowPos(NativeHandle, -2, 0, 0, 0, 0, 0x0013);
            if (Environment.GetEnvironmentVariable("FILESMATE_PHYSICAL_POINTER_SMOKE") == "1")
                MenuPointerSetCursorPos(cursor.X, cursor.Y);
            if (foreground != 0) MenuPointerSetForegroundWindow(foreground);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "menu-pointer-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task Probe(MenuFlyout menu, string label)
        {
            var items = menu.Items.OfType<MenuFlyoutItem>().Where(i => i.IsEnabled).Take(3).ToArray();
            foreach (var item in items)
            {
                item.PointerEntered += (_, args) => events.Add(new { Menu = label, Item = item.Text, Event = "Entered", Point = args.GetCurrentPoint(item).Position });
                item.PointerMoved += (_, args) => events.Add(new { Menu = label, Item = item.Text, Event = "Moved", Point = args.GetCurrentPoint(item).Position });
                var bounds = item.TransformToVisual(Content).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                var scale = item.XamlRoot.RasterizationScale;
                var point = new MenuPointerPoint { X = (int)Math.Round((bounds.X + bounds.Width / 2) * scale), Y = (int)Math.Round((bounds.Y + bounds.Height / 2) * scale) };
                MenuPointerClientToScreen(NativeHandle, ref point);
                var hit = MenuPointerWindowFromPoint(point);
                BrowsingGetWindowThreadProcessId(hit, out var owner);
                events.Add(new { Menu = label, Item = item.Text, Event = "Target", Bounds = bounds, Screen = new { point.X, point.Y },
                    HitWindow = hit.ToInt64(), HitProcess = owner, Process = Environment.ProcessId,
                    PeerBounds = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetBoundingRectangle(),
                    MainPosition = new { AppWindow.Position.X, AppWindow.Position.Y }, MainSize = new { AppWindow.Size.Width, AppWindow.Size.Height } });
                if (owner != Environment.ProcessId) throw new InvalidOperationException("Pointer test point is outside its own window");
                if (Environment.GetEnvironmentVariable("FILESMATE_PHYSICAL_POINTER_SMOKE") == "1")
                {
                    // A popup can open under an unmoved pointer. Force a fresh move
                    // even when a previous run left the cursor at the same point.
                    InjectMove(new MenuPointerPoint { X = point.X - 2, Y = point.Y - 2 });
                    await Task.Delay(40);
                    InjectMove(point);
                }
                else
                {
                    var client = point;
                    MenuPointerScreenToClient(hit, ref client);
                    BrowsingPostMessage(hit, 0x0200, 0, (nint)((client.X & 0xffff) | (client.Y << 16)));
                }
                await Task.Delay(140);
                MenuPointerGetCursorPos(out var observed);
                if (Environment.GetEnvironmentVariable("FILESMATE_PHYSICAL_POINTER_SMOKE") == "1"
                    && (Math.Abs(observed.X - point.X) > 3 || Math.Abs(observed.Y - point.Y) > 3))
                    throw new InvalidOperationException("Desktop pointer changed during the isolated hover check");
                var grid = PolishDescendants(item).OfType<Grid>().Single(g => g.Name == "LayoutRoot");
                var state = VisualStateManager.GetVisualStateGroups(grid).Single(g => g.Name == "CommonStates").CurrentState?.Name;
                checks.Add(new { Menu = label, Item = item.Text, Bounds = bounds, Scale = scale, Screen = new { point.X, point.Y },
                    ObservedPointer = new { observed.X, observed.Y },
                    Window = hit.ToInt64(), State = state,
                    Fill = grid.Background is SolidColorBrush brush ? brush.Color.ToString() : grid.Background?.GetType().Name,
                    Islands = ContentIsland.FindAllForCurrentThread().Where(i => !i.IsClosed).Select(i => new { Window = i.Environment.AppWindowId.Value, Size = i.ActualSize }).ToArray() });
                if (state != "PointerOver")
                {
                    await CaptureNativeMenuWindowAsync("native-" + label + ".png");
                    throw new InvalidOperationException("Actual pointer misses rendered menu item: " + label + "/" + item.Text);
                }
            }
            var outside = new MenuPointerPoint { X = 30, Y = 300 };
            MenuPointerClientToScreen(NativeHandle, ref outside);
            var outsideWindow = MenuPointerWindowFromPoint(outside);
            BrowsingGetWindowThreadProcessId(outsideWindow, out var outsideOwner);
            if (outsideOwner != Environment.ProcessId) throw new InvalidOperationException("Pointer exit point is outside its own window");
            if (Environment.GetEnvironmentVariable("FILESMATE_PHYSICAL_POINTER_SMOKE") == "1")
                InjectMove(outside);
            else
            {
                var client = outside;
                MenuPointerScreenToClient(outsideWindow, ref client);
                BrowsingPostMessage(outsideWindow, 0x0200, 0, (nint)((client.X & 0xffff) | (client.Y << 16)));
            }
            await Task.Delay(140);
            if (items.Any(i => VisualStateManager.GetVisualStateGroups(PolishDescendants(i).OfType<Grid>().Single(g => g.Name == "LayoutRoot")).Single(g => g.Name == "CommonStates").CurrentState?.Name == "PointerOver"))
                throw new InvalidOperationException("Menu hover remains after pointer exits: " + label);
            // RenderTargetBitmap can disturb a native popup's composition/input mapping.
            // Capture only after every real pointer assertion, immediately before closing it.
            await CaptureNativeMenuWindowAsync("pointer-" + label + ".png");
            if (Environment.GetEnvironmentVariable("FILESMATE_PHYSICAL_POINTER_SMOKE") == "1")
            {
                var selected = label.StartsWith("View-", StringComparison.Ordinal) ? items[1] : items[^1];
                await Click(selected, label);
            }
        }
        async Task ProbeSubmenu(MenuFlyoutSubItem submenu, string label)
        {
            submenu.PointerEntered += (_, args) => events.Add(new { Menu = label, Event = "SubmenuEntered", Point = args.GetCurrentPoint(submenu).Position });
            submenu.PointerMoved += (_, args) => events.Add(new { Menu = label, Event = "SubmenuMoved", Point = args.GetCurrentPoint(submenu).Position });
            var point = ScreenCenter(submenu);
            InjectMove(new MenuPointerPoint { X = point.X - 2, Y = point.Y - 2 });
            await Task.Delay(40);
            InjectMove(point);
            var items = submenu.Items.OfType<MenuFlyoutItem>().ToArray();
            for (var attempt = 0; attempt < 80 && !items.All(i => i.IsLoaded && i.ActualWidth > 0); attempt++) await Task.Delay(25);
            MenuPointerGetCursorPos(out var actual);
            var root = PolishDescendants(submenu).OfType<Grid>().Single(g => g.Name == "LayoutRoot");
            report["SubmenuHover-" + label] = new { Target = new { point.X, point.Y }, Actual = new { actual.X, actual.Y },
                State = VisualStateManager.GetVisualStateGroups(root).Single(g => g.Name == "CommonStates").CurrentState?.Name,
                Items = items.Select(i => new { i.IsLoaded, i.ActualWidth }).ToArray() };
            if (!items.All(i => i.IsLoaded && i.ActualWidth > 0))
            {
                await CaptureNativeMenuWindowAsync("native-" + label + ".png");
                if (Math.Abs(actual.X - point.X) > 3 || Math.Abs(actual.Y - point.Y) > 3)
                    throw new InvalidOperationException("Desktop pointer changed during the isolated submenu check");
                throw new InvalidOperationException("Native hover did not open submenu: " + label);
            }
            var alignment = items.Select(item => new
            {
                item.Text,
                Left = PolishDescendants(item).OfType<TextBlock>().Single(t => t.Name == "TextBlock").TransformToVisual(item).TransformPoint(new()).X,
                Height = item.ActualHeight,
            }).ToArray();
            report["SubmenuAlignment-" + label] = alignment;
            if (alignment.Any(i => Math.Abs(i.Left - 28) > .7 || Math.Abs(i.Height - 32) > .7))
                throw new InvalidOperationException("Nested view options are not compact: " + label);
            await CaptureNativeMenuWindowAsync("pointer-" + label + ".png");
            await Click(items[2], label);
        }
        MenuPointerPoint ScreenCenter(FrameworkElement item)
        {
            var bounds = item.TransformToVisual(Content).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            var scale = item.XamlRoot.RasterizationScale;
            var point = new MenuPointerPoint { X = (int)Math.Round((bounds.X + bounds.Width / 2) * scale), Y = (int)Math.Round((bounds.Y + bounds.Height / 2) * scale) };
            MenuPointerClientToScreen(NativeHandle, ref point);
            BrowsingGetWindowThreadProcessId(MenuPointerWindowFromPoint(point), out var owner);
            if (owner != Environment.ProcessId) throw new InvalidOperationException("Native menu point is outside its own window");
            return point;
        }
        async Task Click(MenuFlyoutItem selected, string label)
        {
            var clicked = false;
            void OnClick(object sender, RoutedEventArgs args) => clicked = true;
            selected.Click += OnClick;
            try
            {
                var point = ScreenCenter(selected);
                InjectMove(point);
                await Task.Delay(60);
                MenuPointerGetCursorPos(out var actual);
                BrowsingGetWindowThreadProcessId(MenuPointerWindowFromPoint(actual), out var owner);
                if (owner != Environment.ProcessId || Math.Abs(actual.X - point.X) > 3 || Math.Abs(actual.Y - point.Y) > 3)
                    throw new InvalidOperationException("Desktop pointer changed during the isolated click check");
                MenuPointerInput[] input = [new() { Mouse = new() { Flags = 2 } }, new() { Mouse = new() { Flags = 4 } }];
                if (MenuPointerSendInput(2, input, Marshal.SizeOf<MenuPointerInput>()) != 2) throw new InvalidOperationException("Native click was not delivered");
                for (var attempt = 0; attempt < 80 && !clicked; attempt++) await Task.Delay(25);
                if (!clicked) throw new InvalidOperationException("Actual click misses rendered menu item: " + label + "/" + selected.Text);
                report["NativeClick-" + label] = selected.Text;
            }
            finally { selected.Click -= OnClick; }
        }
        static void InjectMove(MenuPointerPoint point)
        {
            var left = MenuPointerGetSystemMetrics(76); var top = MenuPointerGetSystemMetrics(77);
            var width = MenuPointerGetSystemMetrics(78); var height = MenuPointerGetSystemMetrics(79);
            MenuPointerInput[] input = [new() { Mouse = new()
            {
                X = (int)Math.Round((point.X - left + .5) * 65536 / width),
                Y = (int)Math.Round((point.Y - top + .5) * 65536 / height), Flags = 0xC001,
            } }];
            if (MenuPointerSendInput(1, input, Marshal.SizeOf<MenuPointerInput>()) != 1)
                throw new InvalidOperationException("Native pointer move was not delivered");
        }
    }

    private async Task CaptureNativeMenuWindowAsync(string name)
    {
        MenuPointerGetWindowRect(NativeHandle, out var rect);
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var desktop = MenuPointerGetDC(0); var memory = MenuPointerCreateCompatibleDC(desktop);
        var bitmap = MenuPointerCreateCompatibleBitmap(desktop, width, height);
        var old = MenuPointerSelectObject(memory, bitmap);
        try
        {
            MenuPointerBitBlt(memory, 0, 0, width, height, desktop, rect.Left, rect.Top, 0x00CC0020);
            var pixels = new byte[width * height * 4];
            var info = new MenuPointerBitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            MenuPointerGetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0);
            using var file = File.Open(Path.Combine(AppContext.BaseDirectory, name), FileMode.Create);
            using var stream = file.AsRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
            await encoder.FlushAsync();
        }
        finally
        {
            MenuPointerSelectObject(memory, old); MenuPointerDeleteObject(bitmap); MenuPointerDeleteDC(memory); MenuPointerReleaseDC(0, desktop);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct MenuPointerPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MenuPointerInput { public uint Type; public MenuPointerMouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MenuPointerMouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MenuPointerRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MenuPointerBitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Colors, Important; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool MenuPointerGetWindowRect(nint window, out MenuPointerRect rect);
    [DllImport("user32.dll", EntryPoint = "ScreenToClient")] private static extern bool MenuPointerScreenToClient(nint window, ref MenuPointerPoint point);
    [DllImport("user32.dll", EntryPoint = "SendInput")] private static extern uint MenuPointerSendInput(uint count, [In] MenuPointerInput[] input, int size);
    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")] private static extern int MenuPointerGetSystemMetrics(int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool MenuPointerSetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetDC")] private static extern nint MenuPointerGetDC(nint window);
    [DllImport("user32.dll", EntryPoint = "ReleaseDC")] private static extern int MenuPointerReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")] private static extern nint MenuPointerCreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")] private static extern nint MenuPointerCreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll", EntryPoint = "SelectObject")] private static extern nint MenuPointerSelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll", EntryPoint = "BitBlt")] private static extern bool MenuPointerBitBlt(nint dc, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);
    [DllImport("gdi32.dll", EntryPoint = "GetDIBits")] private static extern int MenuPointerGetDIBits(nint dc, nint bitmap, uint first, uint count, byte[] pixels, ref MenuPointerBitmapInfo info, uint usage);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")] private static extern bool MenuPointerDeleteObject(nint obj);
    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")] private static extern bool MenuPointerDeleteDC(nint dc);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool MenuPointerSetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool MenuPointerGetCursorPos(out MenuPointerPoint point);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")] private static extern bool MenuPointerClientToScreen(nint window, ref MenuPointerPoint point);
    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")] private static extern nint MenuPointerWindowFromPoint(MenuPointerPoint point);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint MenuPointerGetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool MenuPointerSetForegroundWindow(nint window);
}
#endif
