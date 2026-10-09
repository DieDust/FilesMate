using System.Runtime.InteropServices;
using FilesMate.Core.Presentation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace FilesMate.App.Controls.Menus;

internal static class FileContextPopupPlacement
{
    internal static void ShowAt(MenuFlyout menu, FrameworkElement anchor, Point position)
    {
        if (!TryGeometry(anchor, position, out var geometry))
        {
            menu.ShowAt(anchor, new FlyoutShowOptions { Position = position, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
            return;
        }
        void Opening(object? sender, object args)
        {
            var style = new Style(typeof(MenuFlyoutPresenter)) { BasedOn = menu.MenuFlyoutPresenterStyle };
            style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, geometry.Work.Width / geometry.Scale));
            style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, geometry.Work.Height / geometry.Scale));
            menu.MenuFlyoutPresenterStyle = style;
        }
        void Opened(object? sender, object args)
        {
            menu.Opened -= Opened; menu.Opening -= Opening;
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(geometry.Root))
            {
                var presenter = FindMenuPresenter(popup.Child, menu);
                if (presenter is null) continue;
                presenter.UpdateLayout();
                var actual = presenter.TransformToVisual(geometry.Root.Content).TransformPoint(default);
                var final = PopupPlacement.Clamp(geometry.PointerX, geometry.PointerY,
                    presenter.ActualWidth * geometry.Scale, presenter.ActualHeight * geometry.Scale, geometry.Work);
                var x = geometry.OriginX + actual.X * geometry.Scale;
                var y = geometry.OriginY + actual.Y * geometry.Scale;
                if (Math.Abs(final.Left - x) > .5) popup.HorizontalOffset += (final.Left - x) / geometry.Scale;
                if (Math.Abs(final.Top - y) > .5) popup.VerticalOffset += (final.Top - y) / geometry.Scale;
                break;
            }
        }
        menu.Opening += Opening; menu.Opened += Opened;
        menu.ShowAt(anchor, new FlyoutShowOptions { Position = position, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private static MenuFlyoutPresenter? FindMenuPresenter(DependencyObject? root, MenuFlyout menu)
    {
        if (root is null) return null;
        if (root is MenuFlyoutPresenter presenter && presenter.Items.OfType<MenuFlyoutItemBase>().Any(menu.Items.Contains)) return presenter;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindMenuPresenter(VisualTreeHelper.GetChild(root, i), menu) is { } match) return match;
        return null;
    }

    internal static void ShowAt(Flyout menu, FrameworkElement anchor, Point position)
    {
        if (!TryGeometry(anchor, position, out var geometry) || menu.Content is not FrameworkElement content)
        {
            menu.ShowAt(anchor, new FlyoutShowOptions { Position = position, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
            return;
        }
        var originalStyle = menu.FlyoutPresenterStyle;
        var padding = Value(originalStyle, Control.PaddingProperty) is Thickness p ? p : new Thickness(4, 6, 4, 6);
        var border = Value(originalStyle, Control.BorderThicknessProperty) is Thickness b ? b : new Thickness(1);
        var decorationWidth = padding.Left + padding.Right + border.Left + border.Right;
        var decorationHeight = padding.Top + padding.Bottom + border.Top + border.Bottom;
        var maxWidth = Math.Min(Number(originalStyle, FrameworkElement.MaxWidthProperty, 360), geometry.Work.Width / geometry.Scale);
        var minWidth = Math.Min(Number(originalStyle, FrameworkElement.MinWidthProperty, 220), maxWidth);
        var maxHeight = geometry.Work.Height / geometry.Scale;
        // The existing presenter contains a ScrollViewer. A work-area height cap keeps
        // oversized content available without making the popup wider or flipping it.
        void ApplyLimits()
        {
            // FollowHost refreshes the presenter style during Opening. Derive from
            // that current themed style, rather than letting it discard these limits.
            var style = new Style(typeof(FlyoutPresenter)) { BasedOn = menu.FlyoutPresenterStyle };
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, minWidth));
            style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, maxWidth));
            style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, maxHeight));
            menu.FlyoutPresenterStyle = style;
        }
        void Opening(object? sender, object args) => ApplyLimits();
        menu.Opening += Opening;
        content.Measure(new Size(Math.Max(0, maxWidth - decorationWidth), double.PositiveInfinity));
        var measuredWidth = Math.Clamp(content.DesiredSize.Width + decorationWidth, minWidth, maxWidth);
        // Re-measure wrapped labels at the chosen width before deriving the anchor.
        content.Measure(new Size(Math.Max(0, measuredWidth - decorationWidth), double.PositiveInfinity));
        var measuredHeight = Math.Min(content.DesiredSize.Height + decorationHeight, maxHeight);
        var expected = PopupPlacement.Clamp(geometry.PointerX, geometry.PointerY,
            measuredWidth * geometry.Scale, measuredHeight * geometry.Scale, geometry.Work);
        var adjusted = new Point(position.X + (expected.Left - geometry.PointerX) / geometry.Scale,
            position.Y + (expected.Top - geometry.PointerY) / geometry.Scale);
        void Opened(object? sender, object args)
        {
            menu.Opened -= Opened;
            menu.Opening -= Opening;
            // Apply final measured geometry synchronously during opening, retaining
            // Flyout dismissal, focus, keyboard and submenu ownership.
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(geometry.Root))
            {
                var presenter = FindPresenter(popup.Child, menu.Content);
                if (presenter is null) continue;
                void Correct()
                {
                    if (!popup.IsOpen || presenter.ActualWidth <= 0 || presenter.ActualHeight <= 0) return;
                    var actual = presenter.TransformToVisual(geometry.Root.Content).TransformPoint(default);
                    var final = PopupPlacement.Clamp(geometry.PointerX, geometry.PointerY,
                        presenter.ActualWidth * geometry.Scale, presenter.ActualHeight * geometry.Scale, geometry.Work);
                    var actualX = geometry.OriginX + actual.X * geometry.Scale;
                    var actualY = geometry.OriginY + actual.Y * geometry.Scale;
                    if (Math.Abs(final.Left - actualX) > .5) popup.HorizontalOffset += (final.Left - actualX) / geometry.Scale;
                    if (Math.Abs(final.Top - actualY) > .5) popup.VerticalOffset += (final.Top - actualY) / geometry.Scale;
                }
                try
                {
                    presenter.UpdateLayout();
                    Correct();
                }
                catch (Exception error) { App.LogFailure("ContextMenuPlacement", error); }
                break;
            }
        }
        menu.Opened += Opened;
        menu.ShowAt(anchor, new FlyoutShowOptions { Position = adjusted, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private sealed record Geometry(XamlRoot Root, double Scale, int OriginX, int OriginY,
        double PointerX, double PointerY, PopupBounds Work);

    private static bool TryGeometry(FrameworkElement anchor, Point position, out Geometry geometry)
    {
        geometry = null!;
        try
        {
            if (anchor.XamlRoot is not { } root || root.Content is null || root.RasterizationScale <= 0) return false;
            var window = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
            var origin = new NativePoint();
            if (window == 0 || !ClientToScreen(window, ref origin)) return false;
            var logical = anchor.TransformToVisual(root.Content).TransformPoint(position);
            var x = origin.X + logical.X * root.RasterizationScale;
            var y = origin.Y + logical.Y * root.RasterizationScale;
            var area = DisplayArea.GetFromPoint(new PointInt32((int)Math.Round(x), (int)Math.Round(y)), DisplayAreaFallback.Nearest)?.WorkArea;
            if (area is not { Width: > 0, Height: > 0 } work) return false;
            geometry = new(root, root.RasterizationScale, origin.X, origin.Y, x, y, new(work.X, work.Y, work.Width, work.Height));
            return true;
        }
        catch (Exception error) { App.LogFailure("ContextMenuGeometry", error); return false; }
    }

    private static object? Value(Style? style, DependencyProperty property)
    {
        for (; style is not null; style = style.BasedOn)
            foreach (var setter in style.Setters.OfType<Setter>())
                if (setter.Property == property) return setter.Value;
        return null;
    }
    private static double Number(Style? style, DependencyProperty property, double fallback) =>
        Value(style, property) is double number && double.IsFinite(number) && number > 0 ? number : fallback;
    private static FlyoutPresenter? FindPresenter(DependencyObject? root, object content)
    {
        if (root is null) return null;
        if (root is FlyoutPresenter presenter && ReferenceEquals(presenter.Content, content)) return presenter;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPresenter(VisualTreeHelper.GetChild(root, i), content) is { } match) return match;
        return null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
}
