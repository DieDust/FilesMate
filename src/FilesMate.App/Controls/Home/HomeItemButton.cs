using FilesMate.App.Input;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace FilesMate.App.Controls.Home;

public sealed class HomeItemButton : Button
{
    private bool _pointerInput, _moved, _modified, _onName, _releasedOnName, _validRelease;
    private Point _origin, _release;
    private string? _pressedIdentity;
    private ItemOpeningMode _pressedMode;
    private readonly HashSet<TextBlock> _boundNames = [];
    private bool _selected;
    public static readonly DependencyProperty SelectionBrushProperty = DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(HomeItemButton),
        new PropertyMetadata(null, (owner, _) => { var button = (HomeItemButton)owner; if (button._selected) button.Background = button.SelectionBrush; }));
    public Brush? SelectionBrush { get => (Brush?)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public bool IsFolder { get; set; } = true;
    public event EventHandler? ItemInvoked;
    public event EventHandler? SelectionRequested;
    private ItemOpeningMode Mode => App.ExplorerPreferences.OpeningMode(IsFolder);
    private string Identity => Tag switch { HomeFolderItem f => f.Path, HomeDriveItem d => d.Path, string p => p, _ => "" };

    public HomeItemButton()
    {
        Click += (_, _) =>
        {
            SelectionRequested?.Invoke(this, EventArgs.Empty);
            if (!_pointerInput) { ItemInvoked?.Invoke(this, EventArgs.Empty); return; }
            if (!_validRelease || _moved || _modified || ItemActivation.HasSelectionModifier
                || _pressedMode != Mode || _pressedIdentity != Identity)
            { ItemActivation.CancelPendingClick(this); return; }
            if (ItemActivation.ShouldOpen(this, Identity, _release, Mode, _onName && _releasedOnName))
                ItemInvoked?.Invoke(this, EventArgs.Empty);
        };
        Loaded += (_, _) =>
        {
            foreach (var name in Names(this))
                if (_boundNames.Add(name)) ItemActivation.BindName(name, () => Mode, cursor => ProtectedCursor = cursor);
        };
    }

    internal void SetSelected(bool selected)
    {
        _selected = selected;
        if (selected) Background = SelectionBrush;
        else ClearValue(BackgroundProperty);
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pointerInput = true;
            _moved = false;
            _validRelease = false;
            _modified = ItemActivation.HasSelectionModifier;
            _origin = e.GetCurrentPoint(this).Position;
            _onName = NameAt(_origin);
            _pressedIdentity = Identity;
            _pressedMode = Mode;
        }
        else ItemActivation.CancelPendingClick(this);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        if (_pointerInput && (Math.Abs(point.X - _origin.X) > 4 || Math.Abs(point.Y - _origin.Y) > 4)) _moved = true;
        ProtectedCursor = Mode == ItemOpeningMode.SingleClick || (Mode == ItemOpeningMode.NameClick && NameAt(point))
            ? ItemActivation.HandCursor : null;
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        _release = e.GetCurrentPoint(XamlRoot.Content).Position;
        _releasedOnName = NameAt(point);
        _validRelease = point.X >= 0 && point.X < ActualWidth && point.Y >= 0 && point.Y < ActualHeight
            && Math.Abs(point.X - _origin.X) <= 4 && Math.Abs(point.Y - _origin.Y) <= 4;
        base.OnPointerReleased(e); // Button.Click is raised here, after a completed gesture.
        _pointerInput = false;
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // Button may release its capture before raising Click within PointerReleased.
        DispatcherQueue.TryEnqueue(() => _pointerInput = false);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            _pointerInput = false;
            SelectionRequested?.Invoke(this, EventArgs.Empty);
            if (e.Key == VirtualKey.Enter) ItemInvoked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private bool NameAt(Point point) => Names(this).Any(name =>
        name.TransformToVisual(this).TransformBounds(new Rect(0, 0, name.ActualWidth, name.ActualHeight)).Contains(point));

    private static IEnumerable<TextBlock> Names(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { Tag: "ItemName" } name) yield return name;
            foreach (var nested in Names(child)) yield return nested;
        }
    }
}
