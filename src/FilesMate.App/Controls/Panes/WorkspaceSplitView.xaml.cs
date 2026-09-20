using FilesMate.App.Workspace;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.Foundation;

namespace FilesMate.App.Controls.Panes;

public sealed partial class WorkspaceSplitView : UserControl
{
    private const double SplitterVisual = 1;
    private const double SplitterHit = 6;

    private bool _resizing;
    private Point _resizeStart;
    private double _resizeStartRatio;

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout),
        typeof(WorkspaceLayoutKind),
        typeof(WorkspaceSplitView),
        new PropertyMetadata(WorkspaceLayoutKind.Single, OnLayoutChanged));

    public static readonly DependencyProperty SplitRatioProperty = DependencyProperty.Register(
        nameof(SplitRatio),
        typeof(double),
        typeof(WorkspaceSplitView),
        new PropertyMetadata(0.5, OnSplitRatioChanged));

    public static readonly DependencyProperty LeftContentProperty = DependencyProperty.Register(
        nameof(LeftContent),
        typeof(UIElement),
        typeof(WorkspaceSplitView),
        new PropertyMetadata(null, OnLeftContentChanged));

    public static readonly DependencyProperty RightContentProperty = DependencyProperty.Register(
        nameof(RightContent),
        typeof(UIElement),
        typeof(WorkspaceSplitView),
        new PropertyMetadata(null, OnRightContentChanged));

    public WorkspaceSplitView()
    {
        InitializeComponent();
        ApplyLayout();
    }

    public event EventHandler<double>? SplitRatioChanged;

    public WorkspaceLayoutKind Layout
    {
        get => (WorkspaceLayoutKind)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public double SplitRatio
    {
        get => (double)GetValue(SplitRatioProperty);
        set => SetValue(SplitRatioProperty, WorkspaceLayoutMath.ClampRatio(value));
    }

    public UIElement? LeftContent
    {
        get => (UIElement?)GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }

    public UIElement? RightContent
    {
        get => (UIElement?)GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    public void ResetSplitRatio() => SplitRatio = 0.5;

    private static void OnLeftContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceSplitView view)
        {
            view.LeftPresenter?.SetValue(ContentPresenter.ContentProperty, e.NewValue);
        }
    }

    private static void OnRightContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceSplitView view)
        {
            view.RightPresenter?.SetValue(ContentPresenter.ContentProperty, e.NewValue);
        }
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceSplitView view)
        {
            view.ApplyLayout();
        }
    }

    private static void OnSplitRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkspaceSplitView view)
        {
            view.ApplyRatio();
            view.SplitRatioChanged?.Invoke(view, view.SplitRatio);
        }
    }

    private void ApplyLayout()
    {
        if (Layout == WorkspaceLayoutKind.Horizontal)
        {
            LeftPresenter.SetValue(Grid.RowProperty, 0);
            LeftPresenter.SetValue(Grid.ColumnProperty, 0);
            RightPresenter.SetValue(Grid.RowProperty, 2);
            RightPresenter.SetValue(Grid.ColumnProperty, 0);
            Separator.SetValue(Grid.RowProperty, 1);
            Separator.SetValue(Grid.ColumnProperty, 0);
            LeftColumn.Width = new GridLength(1, GridUnitType.Star);
            SeparatorColumn.Width = new GridLength(0);
            RightColumn.Width = new GridLength(1, GridUnitType.Star);
            TopRow.Height = new GridLength(SplitRatio, GridUnitType.Star);
            HorizontalSeparatorRow.Height = new GridLength(SplitterVisual);
            BottomRow.Height = new GridLength(1 - SplitRatio, GridUnitType.Star);
            ApplySplitterChrome(horizontal: true);
        }
        else
        {
            LeftPresenter.SetValue(Grid.RowProperty, 0);
            LeftPresenter.SetValue(Grid.ColumnProperty, 0);
            RightPresenter.SetValue(Grid.RowProperty, 0);
            RightPresenter.SetValue(Grid.ColumnProperty, 2);
            Separator.SetValue(Grid.RowProperty, 0);
            Separator.SetValue(Grid.ColumnProperty, 1);
            TopRow.Height = new GridLength(1, GridUnitType.Star);
            HorizontalSeparatorRow.Height = new GridLength(0);
            BottomRow.Height = new GridLength(0);
            ApplySplitterChrome(horizontal: false);
            SeparatorColumn.Width = Layout == WorkspaceLayoutKind.Single
                ? new GridLength(0)
                : new GridLength(SplitterVisual);
            RightColumn.Width = Layout == WorkspaceLayoutKind.Single
                ? new GridLength(0)
                : new GridLength(1 - SplitRatio, GridUnitType.Star);
            LeftColumn.Width = Layout == WorkspaceLayoutKind.Single
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(SplitRatio, GridUnitType.Star);
        }

        RightPresenter.Visibility = WorkspaceLayoutMath.IsSecondaryVisible(Layout)
            ? Visibility.Visible
            : Visibility.Collapsed;
        Separator.Visibility = RightPresenter.Visibility;
    }

    private void ApplyRatio()
    {
        if (Layout == WorkspaceLayoutKind.Horizontal)
        {
            TopRow.Height = new GridLength(SplitRatio, GridUnitType.Star);
            BottomRow.Height = new GridLength(1 - SplitRatio, GridUnitType.Star);
        }
        else if (Layout != WorkspaceLayoutKind.Single)
        {
            LeftColumn.Width = new GridLength(SplitRatio, GridUnitType.Star);
            RightColumn.Width = new GridLength(1 - SplitRatio, GridUnitType.Star);
        }
    }

    private void Separator_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Layout == WorkspaceLayoutKind.Single)
        {
            return;
        }

        _resizing = Separator.CapturePointer(e.Pointer);
        if (!_resizing)
        {
            return;
        }

        _resizeStart = e.GetCurrentPoint(Root).Position;
        _resizeStartRatio = SplitRatio;
        e.Handled = true;
    }

    private void Separator_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        var position = e.GetCurrentPoint(Root).Position;
        var delta = Layout == WorkspaceLayoutKind.Horizontal
            ? position.Y - _resizeStart.Y
            : position.X - _resizeStart.X;
        var available = Layout == WorkspaceLayoutKind.Horizontal
            ? Root.ActualHeight - HorizontalSeparatorRow.ActualHeight
            : Root.ActualWidth - SeparatorColumn.ActualWidth;
        if (available > 0)
        {
            SplitRatio = WorkspaceLayoutMath.ClampRatio(_resizeStartRatio + (delta / available));
        }

        e.Handled = true;
    }

    private void Separator_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        StopResizing(e.Pointer);
        e.Handled = true;
    }

    private void Separator_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        StopResizing(e.Pointer);

    private void Separator_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ResetSplitRatio();
        e.Handled = true;
    }

    private void StopResizing(Pointer pointer)
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        Separator.ReleasePointerCapture(pointer);
    }

    private void ApplySplitterChrome(bool horizontal)
    {
        if (Separator is null || SeparatorLine is null)
        {
            return;
        }

        Separator.SetHorizontal(horizontal);
        if (horizontal)
        {
            Separator.Width = double.NaN;
            Separator.Height = SplitterHit;
            Separator.HorizontalAlignment = HorizontalAlignment.Stretch;
            Separator.VerticalAlignment = VerticalAlignment.Center;
            SeparatorLine.Width = double.NaN;
            SeparatorLine.Height = SplitterVisual;
            SeparatorLine.HorizontalAlignment = HorizontalAlignment.Stretch;
            SeparatorLine.VerticalAlignment = VerticalAlignment.Center;
            return;
        }

        Separator.Width = SplitterHit;
        Separator.Height = double.NaN;
        Separator.HorizontalAlignment = HorizontalAlignment.Center;
        Separator.VerticalAlignment = VerticalAlignment.Stretch;
        SeparatorLine.Width = SplitterVisual;
        SeparatorLine.Height = double.NaN;
        SeparatorLine.HorizontalAlignment = HorizontalAlignment.Center;
        SeparatorLine.VerticalAlignment = VerticalAlignment.Stretch;
    }
}
