using Loc = FilesMate.App.Localization.StringTable;
using System.Runtime.InteropServices;
using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using FilesMate.App.Theming;
using Windows.System;

namespace FilesMate.App.Controls.Preview;

public sealed class QuickPreviewWindow : Window
{
    private readonly PreviewPane _pane = new();
    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly PreviewService _service = new([new DevicePreviewProvider(), new ImagePreviewProvider(), new TextPreviewProvider(), new PdfPreviewProvider(), new OfficePreviewProvider(), new MediaPreviewProvider(), new PropertiesPreviewProvider()]);
    private long _generation;
    private bool _closed;
    private bool _closing;
    private readonly Grid _root;
    private readonly nint _handle;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _motion;
    private byte _opacity;
    private string? _path;
    private readonly List<FrameworkElement> _headerButtons = [];
    private Windows.Graphics.RectInt32[] _dragRects = [], _inputRects = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _foregroundCheck;
    private readonly nint _ownerHandle;
    private bool _wasForeground;
    public bool RestoreOwnerFocus { get; private set; } = true;
    public event EventHandler<int>? NavigateFile;

    public QuickPreviewWindow(MainWindow owner)
    {
        var root = _root = (Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Background="{ThemeResource FilesMate.SearchPanel.BackgroundBrush}"
                />
            """);
        var ownerRoot = owner.Content as FrameworkElement;
        root.RequestedTheme = ownerRoot?.ActualTheme ?? ElementTheme.Default;
        void OwnerThemeChanged(FrameworkElement sender, object args) => root.RequestedTheme = sender.ActualTheme;
        if (ownerRoot is not null) ownerRoot.ActualThemeChanged += OwnerThemeChanged;
        _ownerHandle = owner.NativeHandle;
        var layout = new Grid { Padding = new Thickness(12, 8, 12, 12), RowSpacing = 4 };
        root.Children.Add(layout);
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { MinHeight = 32, ColumnSpacing = 6, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_title);
        void Button(string label, string hint, int column, Action action)
        {
            var button = new Button { Content = new FontIcon { Glyph = label, FontSize = 12 }, Width = 30, Height = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(8), Style = (Style)Application.Current.Resources["QuietButtonStyle"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, hint);
            ToolTipService.SetToolTip(button, hint); button.Click += (_, _) => action();
            _headerButtons.Add(button);
            Grid.SetColumn(button, column); header.Children.Add(button);
        }
        Button("\uE76B", Loc.Get("PreviousFile"), 1, () => NavigateFile?.Invoke(this, -1));
        Button("\uE76C", Loc.Get("NextFile"), 2, () => NavigateFile?.Invoke(this, 1));
        Button("\uE8BB", Loc.Get("ClosePreview"), 3, Close);
        layout.Children.Add(header); Grid.SetRow(_pane, 1); layout.Children.Add(_pane);
        AddResizeHandles(root);
        _pane.UseAsQuickPreview(); _pane.Attach(_service); _pane.SetVisible(true); _pane.CloseRequested += (_, _) => Close();
        Content = root;
        AppTypography.Track(root);
        ExtendsContentIntoTitleBar = true; SetTitleBar(header);
        var presenter = Microsoft.UI.Windowing.OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false); presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(owner.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var dpi = GetDpiForWindow(owner.NativeHandle) / 96d;
        var width = Math.Min((int)(600*dpi), (int)(work.Width*.72)); var height = Math.Min((int)(500*dpi), (int)(work.Height*.76));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X+(work.Width-width)/2,work.Y+(work.Height-height)/2,width,height));
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowLongPtrW(_handle, -8, owner.NativeHandle);
        var noBorder = 0xFFFFFFFEu; // DWMWA_COLOR_NONE: retain resize behavior without a native outline.
        DwmSetWindowAttribute(_handle, 34, ref noBorder, sizeof(uint));
        // Fade the entire native window, including WebView2 and its background.
        SetWindowLongPtrW(_handle, -20, GetWindowLongPtrW(_handle, -20) | 0x80000);
        SetLayeredWindowAttributes(_handle, 0, 0, 2);
        // Clip the HWND itself. Rounding only the XAML grid exposes the black native
        // surface at its corners, particularly while using layered-window fades.
        UpdateWindowRegion();
        AppWindow.Changed += (_, args) => { if (args.DidSizeChange || args.DidPositionChange) UpdateWindowRegion(); };
        root.LayoutUpdated += (_, _) => UpdateDragRegions();
        _foregroundCheck = DispatcherQueue.CreateTimer();
        _foregroundCheck.Interval = TimeSpan.FromMilliseconds(160);
        _foregroundCheck.Tick += (_, _) =>
        {
            var foreground = GetForegroundWindow();
            var inside = IsPreviewInteraction(foreground);
            if (inside) _wasForeground = true;
            else if (_wasForeground && foreground != 0) { TraceDismiss($"foreground={foreground} preview={_handle} owner={_ownerHandle}"); Dismiss(); }
            if (IsIconic(_ownerHandle)) Dismiss();
        };
        _foregroundCheck.Start();
        Activated += (_, args) => { if (args.WindowActivationState != WindowActivationState.Deactivated) _wasForeground = true; };
        root.Loaded += (_, _) => { if (!_closing) AnimateWindowOpacity(255, (int)FilesMate.App.Animations.MotionDurations.Standard.TotalMilliseconds, null); };
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if(e.Key is VirtualKey.Escape or VirtualKey.Space) { e.Handled=true; Close(); }
            else if(e.Key is VirtualKey.Left or VirtualKey.Right) { e.Handled=true; NavigateFile?.Invoke(this,e.Key==VirtualKey.Left?-1:1); }
        }), false);
        Closed += async (_, _) =>
        {
            if (ownerRoot is not null) ownerRoot.ActualThemeChanged -= OwnerThemeChanged;
            _closed=true; _motion?.Stop(); _foregroundCheck.Stop(); _pane.SetVisible(false); await _service.DisposeAsync();
        };
    }

    public void Dismiss() { RestoreOwnerFocus = false; Close(); }

    [System.Diagnostics.Conditional("FILESMATE_UI_TEST")]
    private static void TraceDismiss(string reason) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "preview-dismiss.log"), reason + "\n");

    internal bool IsPreviewInteraction(nint window) => window == _handle || window == _ownerHandle
        || IsChild(_handle, window) || IsChild(_ownerHandle, window) || _pane.OwnsNativeWindow(window);

    private void UpdateDragRegions()
    {
        if (_closing || _root.XamlRoot is not { } root || _pane.PreviewViewport.ActualWidth <= 0) return;
        var scale = root.RasterizationScale;
        var bottom = _pane.PreviewViewport.TransformToVisual(_root).TransformPoint(default).Y - 2;
        if (bottom <= 8) return;
        var caption = new List<Windows.Graphics.RectInt32> { new((int)(10 * scale), (int)(8 * scale),
            Math.Max(1, (int)((_root.ActualWidth - 20) * scale)), (int)((bottom - 8) * scale)) };
        var input = new List<Windows.Graphics.RectInt32>();
        foreach (var control in _headerButtons.Concat(_pane.HeaderControls))
        {
            if (control.Visibility != Visibility.Visible || control.ActualWidth <= 0) continue;
            var bounds = control.TransformToVisual(_root).TransformBounds(new(0, 0, control.ActualWidth, control.ActualHeight));
            input.Add(new((int)Math.Floor(bounds.X * scale), (int)Math.Floor(bounds.Y * scale),
                (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale)));
        }
        // Keep caption and interactive regions disjoint. A caption overlap can
        // consume clicks with a custom borderless presenter, despite Passthrough.
        foreach (var control in input)
        {
            var remaining = new List<Windows.Graphics.RectInt32>();
            foreach (var rect in caption)
            {
                var left = Math.Max(rect.X, control.X); var top = Math.Max(rect.Y, control.Y);
                var right = Math.Min(rect.X + rect.Width, control.X + control.Width);
                var end = Math.Min(rect.Y + rect.Height, control.Y + control.Height);
                if (right <= left || end <= top) { remaining.Add(rect); continue; }
                void Add(int x, int y, int width, int height) { if (width > 0 && height > 0) remaining.Add(new(x, y, width, height)); }
                Add(rect.X, rect.Y, rect.Width, top - rect.Y);
                Add(rect.X, end, rect.Width, rect.Y + rect.Height - end);
                Add(rect.X, top, left - rect.X, end - top);
                Add(right, top, rect.X + rect.Width - right, end - top);
            }
            caption = remaining;
        }
        if (_dragRects.SequenceEqual(caption) && _inputRects.SequenceEqual(input)) return;
        _dragRects = [.. caption]; _inputRects = [.. input];
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        source.SetRegionRects(NonClientRegionKind.Caption, _dragRects);
        source.SetRegionRects(NonClientRegionKind.Passthrough, _inputRects);
    }

    public new void Close()
    {
        if (_closed || _closing) return;
        _closing = true;
        _foregroundCheck.Stop();
        _root.IsHitTestVisible = false;
        AnimateWindowOpacity(0, (int)FilesMate.App.Animations.MotionDurations.Fast.TotalMilliseconds, () => base.Close());
    }

    private void AnimateWindowOpacity(byte target, int milliseconds, Action? completed)
    {
        _motion?.Stop();
        milliseconds = (int)App.Motion.Resolve(TimeSpan.FromMilliseconds(milliseconds)).TotalMilliseconds;
        if (milliseconds <= 0)
        {
            _opacity = target;
            SetLayeredWindowAttributes(_handle, 0, target, 2);
            _pane.SetNativePreviewOpacity(target);
            completed?.Invoke();
            return;
        }
        var start = _opacity;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _motion = DispatcherQueue.CreateTimer();
        _motion.Interval = TimeSpan.FromMilliseconds(16);
        _motion.Tick += (timer, _) =>
        {
            var progress = Math.Min(1, watch.Elapsed.TotalMilliseconds / milliseconds);
            var eased = 1 - Math.Pow(1 - progress, 3);
            _opacity = (byte)Math.Clamp(Math.Round(start + (target - start) * eased), 0, 255);
            SetLayeredWindowAttributes(_handle, 0, _opacity, 2);
            _pane.SetNativePreviewOpacity(_opacity);
            if (progress >= 1) { timer.Stop(); completed?.Invoke(); }
        };
        _motion.Start();
    }

    public async Task LoadAsync(string path)
    {
        if(_closed || _closing || string.Equals(_path,path,StringComparison.OrdinalIgnoreCase))return;
        _path=path; Title=_title.Text=FilesMate.App.Navigation.LocationCaption.Title(path); ToolTipService.SetToolTip(_title,Title);
        await _pane.LoadAsync(path,++_generation);
    }

    private void UpdateWindowRegion()
    {
        if (_closed || !GetWindowRect(_handle, out var bounds) || !GetClientRect(_handle, out var client)) return;
        var origin = new WindowPoint();
        if (!ClientToScreen(_handle, ref origin)) return;
        var left = origin.X - bounds.Left;
        var top = origin.Y - bounds.Top;
        var diameter = (int)Math.Round(24 * GetDpiForWindow(_handle) / 96d);
        // Exclude the resize frame, including WinUI's one-pixel extended top
        // frame, as well as the corner backdrop.
        var region = CreateRoundRectRgn(left, top + 1, left + client.Right + 1, top + client.Bottom + 1, diameter, diameter);
        // Windows owns the region after a successful SetWindowRgn call.
        if (region != 0 && SetWindowRgn(_handle, region, true) == 0) DeleteObject(region);
    }

    private void AddResizeHandles(Grid root)
    {
        foreach (var (horizontal, vertical) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var corner = horizontal != 0 && vertical != 0;
            var grip = new PreviewResizeGrip(horizontal, vertical)
            {
                Width = horizontal == 0 ? double.NaN : corner ? 24 : 10,
                Height = vertical == 0 ? double.NaN : corner ? 24 : 10,
                HorizontalAlignment = horizontal < 0 ? HorizontalAlignment.Left : horizontal > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = vertical < 0 ? VerticalAlignment.Top : vertical > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grip, Loc.Get("ResizePreview"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(grip, $"PreviewResize_{horizontal}_{vertical}");
            ToolTipService.SetToolTip(grip, Loc.Get("ResizePreview"));
            if (horizontal == 1 && vertical == 1)
                grip.Children.Add(new TextBlock { Text = "◢", FontSize = 12, Opacity = .4, Margin = new Thickness(0, 0, 5, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
            uint? pointer = null;
            Windows.Foundation.Point start = default;
            Windows.Graphics.RectInt32 original = default;
            Windows.Foundation.Point ScreenPoint(PointerRoutedEventArgs e)
            {
                var point = e.GetCurrentPoint(root).Position;
                var origin = new WindowPoint(); ClientToScreen(_handle, ref origin);
                var scale = GetDpiForWindow(_handle) / 96d;
                return new(origin.X + point.X * scale, origin.Y + point.Y * scale);
            }
            grip.PointerPressed += (_, e) =>
            {
                if (_closing || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || !grip.CapturePointer(e.Pointer)) return;
                pointer = e.Pointer.PointerId; start = ScreenPoint(e);
                original = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
                e.Handled = true;
            };
            grip.PointerMoved += (_, e) =>
            {
                if (pointer != e.Pointer.PointerId) return;
                var point = ScreenPoint(e);
                var scale = GetDpiForWindow(_handle) / 96d;
                var width = horizontal == 0 ? original.Width : Math.Max((int)(360 * scale), original.Width + horizontal * (int)Math.Round(point.X - start.X));
                var height = vertical == 0 ? original.Height : Math.Max((int)(280 * scale), original.Height + vertical * (int)Math.Round(point.Y - start.Y));
                AppWindow.MoveAndResize(new(original.X + (horizontal < 0 ? original.Width - width : 0), original.Y + (vertical < 0 ? original.Height - height : 0), width, height));
                e.Handled = true;
            };
            grip.PointerReleased += (_, e) => { if (pointer == e.Pointer.PointerId) { pointer = null; grip.ReleasePointerCapture(e.Pointer); e.Handled = true; } };
            grip.PointerCaptureLost += (_, _) => pointer = null;
            grip.PointerCanceled += (_, _) => pointer = null;
            root.Children.Add(grip);
        }
    }

    private sealed class PreviewResizeGrip : Grid
    {
        public PreviewResizeGrip(int horizontal, int vertical)
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(horizontal == 0
                ? Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth : vertical == 0
                ? Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast : horizontal == vertical
                ? Microsoft.UI.Input.InputSystemCursorShape.SizeNorthwestSoutheast
                : Microsoft.UI.Input.InputSystemCursorShape.SizeNortheastSouthwest);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref WindowPoint point);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window,nint region,bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window,int attribute,ref uint value,int size);

    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window,int index,nint value);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window,int index);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window,uint color,byte alpha,uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
}
