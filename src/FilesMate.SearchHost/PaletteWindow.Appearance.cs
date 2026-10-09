using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FilesMate.App.Models;
using FilesMate.Search;
using Microsoft.Win32;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private bool _nativeRoundingApplied;
    private SearchRankingPreferencesWatcher? _appearanceWatcher;
    private readonly List<WeakReference<FrameworkElement>> _appearancePopups = new();
    private readonly DispatcherTimer _appearanceTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private AppearanceSettings? _appearancePreview;

    private AppearanceSettings EffectiveAppearance()
    {
        // File notifications rebase unrelated committed fields (font/icons) without ending the
        // transient color/material preview. Ordinary lease heartbeats never enter this path.
        var committed = PaletteAppearance.Load(_host.Profile);
        return _appearancePreview is { } preview ? AppearancePreviewCodec.Overlay(committed, preview) : committed;
    }

    // Preview is a transient host-owned lease. It never writes appearance.json,
    // and unrelated file/system notifications must not replace it while active.
    internal void ApplyAppearancePreview(AppearanceSettings settings)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => ApplyAppearancePreview(settings)));
            return;
        }
        _appearancePreview = settings;
        _appearanceTimer.Stop();
        RefreshPaletteAppearance();
    }

    internal void ClearAppearancePreview()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(ClearAppearancePreview));
            return;
        }
        _appearancePreview = null;
        _appearanceTimer.Stop();
        RefreshPaletteAppearance();
    }

    private void InitializeAppearanceTracking()
    {
        _appearanceTimer.Tick += (_, _) => { _appearanceTimer.Stop(); RefreshPaletteAppearance(); };
        _appearanceWatcher = new SearchRankingPreferencesWatcher(Path.Combine(_host.Profile, "appearance.json"), QueueAppearanceRefresh);
        SystemParameters.StaticPropertyChanged += SystemAppearanceChanged;
        SystemEvents.UserPreferenceChanged += UserAppearanceChanged;
        Closed += (_, _) =>
        {
            _appearanceTimer.Stop();
            _appearanceWatcher?.Dispose();
            SystemParameters.StaticPropertyChanged -= SystemAppearanceChanged;
            SystemEvents.UserPreferenceChanged -= UserAppearanceChanged;
        };
    }

    private void SystemAppearanceChanged(object? sender, PropertyChangedEventArgs args) => QueueAppearanceRefresh();
    private void UserAppearanceChanged(object sender, UserPreferenceChangedEventArgs args) => QueueAppearanceRefresh();
    private void QueueAppearanceRefresh()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _appearanceTimer.Stop();
            _appearanceTimer.Start();
        }));
    }

    private void RefreshPaletteAppearance()
    {
        _appearance = EffectiveAppearance();
        PaletteAppearance.Apply(Resources, _appearance);
        if (_icons.UseBundledIcons != _appearance.UseBundledFileIcons)
        {
            CancelVisibleIcons();
            _icons.UseBundledIcons = _appearance.UseBundledFileIcons;
            foreach (var row in _rows) { row.Icon = _icons.Fallback(row.Hit); row.IconLoaded = false; }
            if (IsVisible) QueueVisibleIcons();
        }
        if (!PaletteMotionEnabled)
        {
            ResetPaletteMotion();
            ResetSearchAnimations();
            SearchActivity.IsIndeterminate = false;
        }
        ApplyNativeFrame();
        if (PreviewPopup.IsOpen) ApplyPreviewRoundRegion();
        _appearancePopups.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _appearancePopups)
            if (reference.TryGetTarget(out var surface) && surface.IsVisible) ApplyPopupAppearance(surface);
        RefreshReaderDocumentTheme();
        RefreshCodePreviewTheme();
        if (IsVisible) QueueCenter();
    }

    private void ApplyNativeFrame()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _nativeRoundingApplied = ConfigureNativeSurface(hwnd, out var backdropApplied);
        // One compositor owns the outline and the rounded clip. Keeping an
        // independently antialiased XAML border can produce doubled corner arcs.
        Shell.CornerRadius = _nativeRoundingApplied ? new CornerRadius(0) : (CornerRadius)Resources["InputCorner"];
        Shell.BorderThickness = new Thickness(_nativeRoundingApplied ? 0 : 1);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = backdropApplied ? Colors.Transparent : OpaqueCanvasColor();
        if (!backdropApplied) Resources["GlassSurface"] = Resources["PaletteCanvas"];
        ApplyRoundRegion();
    }

    private bool ConfigureNativeSurface(IntPtr hwnd, out bool backdropApplied)
    {
        var rounded = 2;
        var rounding = Native.DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int)) >= 0;
        var dark = PaletteAppearance.IsDark(_appearance) ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var outline = Resources["Outline"] is SolidColorBrush brush ? brush.Color : OpaqueSurfaceColor();
        var border = outline.R | outline.G << 8 | outline.B << 16;
        Native.DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        var backdrop = PaletteAppearance.NativeBackdrop(_appearance);
        backdropApplied = backdrop != 1 && Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) >= 0;
        if (!backdropApplied)
        {
            var none = 1;
            Native.DwmSetWindowAttribute(hwnd, 38, ref none, sizeof(int));
        }
        var margins = backdropApplied ? new Native.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Native.Margins();
        Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        return rounding;
    }

    private Color OpaqueSurfaceColor()
    {
        if (SystemParameters.HighContrast) return SystemColors.WindowColor;
        return (Color)ColorConverter.ConvertFromString(AccentPalette.ToHex(SkinPalette.For(PaletteAppearance.IsDark(_appearance), _appearance).Floating));
    }

    private Color OpaqueCanvasColor()
    {
        if (Resources["PaletteCanvas"] is SolidColorBrush brush)
        {
            var color = brush.Color; color.A = 255; return color;
        }
        return OpaqueSurfaceColor();
    }

    private void ApplyRoundRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (_nativeRoundingApplied) { Native.SetWindowRgn(hwnd, IntPtr.Zero, true); return; }
        if (!Native.GetWindowRect(hwnd, out var rect)) return;
        var diameter = (int)Math.Round(Shell.CornerRadius.TopLeft * 2 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var region = Native.CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1, diameter, diameter);
        if (region != IntPtr.Zero && Native.SetWindowRgn(hwnd, region, true) == 0) Native.DeleteObject(region);
    }

    private void PopupSurface_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement surface)
        {
            if (!_appearancePopups.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, surface)))
                _appearancePopups.Add(new WeakReference<FrameworkElement>(surface));
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyPopupAppearance(surface)));
        }
    }

    // Popups hosting WebView2 stay non-layered. Standard WPF layered popups do
    // not reliably support DWM backdrops and use an explicit readable solid veil.
    private bool ApplyPopupAppearance(FrameworkElement surface)
    {
        if (PresentationSource.FromVisual(surface) is not HwndSource source) return false;
        var layered = (GetWindowLongW(source.Handle, -20) & 0x80000) != 0;
        if (layered)
        {
            if (surface is Border fallback) fallback.Background = new SolidColorBrush(OpaqueSurfaceColor());
            return false;
        }
        var rounded = ConfigureNativeSurface(source.Handle, out var backdropApplied);
        if (source.CompositionTarget is { } target)
            target.BackgroundColor = backdropApplied ? Colors.Transparent : OpaqueSurfaceColor();
        if (surface is Border border)
        {
            border.Background = backdropApplied ? (Brush)Resources["PreviewSurface"] : new SolidColorBrush(OpaqueSurfaceColor());
            border.CornerRadius = rounded ? new CornerRadius(0) : (CornerRadius)Resources["SurfaceCorner"];
            border.BorderThickness = new Thickness(rounded ? 0 : 1);
        }
        if (rounded) Native.SetWindowRgn(source.Handle, IntPtr.Zero, true);
        return rounded;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);
}
