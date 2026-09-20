using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.Favorites;

/// <summary>Keeps the virtualized lists bounded while allowing the organizer to use the available window.</summary>
internal sealed class FavoritesManagerDialog : ContentDialog
{
    private readonly Grid _surface = new();
    private readonly XamlRoot _root;
    private double? _preferredHeight;
    private bool _resized;

    public FavoritesManagerDialog(FavoritesManager manager, XamlRoot root)
    {
        _root = root;
        XamlRoot = root;
        DefaultButton = ContentDialogButton.None;
        var saved = App.Features.FavoritesManagerHeight;
        _preferredHeight = saved is > 0 && double.IsFinite(saved.Value) ? saved : null;
        Resources["ContentDialogPadding"] = new Thickness(0);
        manager.Margin = new Thickness(24);
        _surface.Children.Add(manager);
        AddEdge(VerticalAlignment.Top, -1, Loc.Get("Favorites_ResizeTop"));
        AddEdge(VerticalAlignment.Bottom, 1, Loc.Get("Favorites_ResizeBottom"));
        Content = _surface;
        UpdateSize();
        Opened += (_, _) => _root.Changed += RootChanged;
        Closed += (_, _) =>
        {
            _root.Changed -= RootChanged;
            if (_resized && _preferredHeight is { } height)
            {
                try { App.SetFavoritesManagerHeight(height); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { System.Diagnostics.Debug.WriteLine(error); }
            }
        };
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateSize();

    private void UpdateSize()
    {
        var maxWidth = Math.Max(320, _root.Size.Width - 64);
        var maxHeight = Math.Max(240, _root.Size.Height - 80);
        Resources["ContentDialogMaxWidth"] = maxWidth + 4;
        Resources["ContentDialogMaxHeight"] = maxHeight + 4;
        _surface.Width = Math.Clamp(_root.Size.Width * .86, Math.Min(620, maxWidth), maxWidth);
        _surface.Height = Math.Clamp(_preferredHeight ?? _root.Size.Height * .8, Math.Min(360, maxHeight), maxHeight);
    }

    private void AddEdge(VerticalAlignment alignment, int direction, string name)
    {
        var edge = new ResizeEdge { Height = 8, VerticalAlignment = alignment, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        AutomationProperties.SetName(edge, name);
        ToolTipService.SetToolTip(edge, Loc.Get("ResizeVertical"));
        uint? pointer = null;
        double origin = 0, height = 0;
        edge.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(edge).Properties.IsLeftButtonPressed || !edge.CapturePointer(e.Pointer)) return;
            pointer = e.Pointer.PointerId;
            origin = e.GetCurrentPoint(_root.Content).Position.Y;
            height = _surface.ActualHeight;
            e.Handled = true;
        };
        edge.PointerMoved += (_, e) =>
        {
            if (pointer != e.Pointer.PointerId) return;
            // The dialog stays centered, so each edge travels half the height change.
            var requested = height + direction * 2 * (e.GetCurrentPoint(_root.Content).Position.Y - origin);
            var maximum = Math.Max(240, _root.Size.Height - 80);
            _preferredHeight = Math.Clamp(requested, Math.Min(360, maximum), maximum);
            _resized = true;
            UpdateSize();
            e.Handled = true;
        };
        edge.PointerReleased += (_, e) => { if (pointer == e.Pointer.PointerId) { pointer = null; edge.ReleasePointerCapture(e.Pointer); e.Handled = true; } };
        edge.PointerCaptureLost += (_, _) => pointer = null;
        _surface.Children.Add(edge);
    }

    private sealed class ResizeEdge : UserControl
    {
        public ResizeEdge()
        {
            ProtectedCursor = DesktopCursors.SizeNorthSouth;
            Content = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        }
    }
}
