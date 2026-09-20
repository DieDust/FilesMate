using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Input;
using FilesMate.App.Models;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private const string ColumnDragFormat = "FilesMate.DetailsColumn";
    private readonly string _columnDragToken = Guid.NewGuid().ToString("N");
    private DetailsColumn[] _detailColumns = DetailsColumn.Defaults();
    private readonly Dictionary<DetailsColumnId, (Button Button, Border Resize)> _columnHeaders = [];
    private readonly Dictionary<DetailsColumnId, FontIcon> _extraSortGlyphs = [];
    private EntrySort _headerSort = EntrySort.Name;
    private bool _suppressHeaderClick;
    private bool _columnMenuOpen;
    private Border? _headerDivider;
    private Border? _columnDropIndicator;
    private double VisibleColumnWidth => _detailColumns.Where(c => c.Visible).Sum(c => c.Width);

    public DetailsColumn[] GetColumns() => _detailColumns.ToArray();

    public void SetColumns(DetailsColumn[]? columns)
    {
        var normalized = DetailsColumn.Normalize(columns ?? LegacyColumns());
        if (_detailColumns.SequenceEqual(normalized)) return;
        _detailColumns = normalized;
        ApplyDetailsColumns(false);
    }

    private static DetailsColumn[] LegacyColumns()
    {
        var prefs = App.ExplorerPreferences;
        return DetailsColumn.Defaults().Select(c => c with { Width = c.Id switch
        {
            DetailsColumnId.Name => prefs.DetailsNameWidth, DetailsColumnId.Modified => prefs.DetailsModifiedWidth,
            DetailsColumnId.Type => prefs.DetailsTypeWidth, DetailsColumnId.Size => prefs.DetailsSizeWidth, _ => c.Width
        }}).ToArray();
    }

    private void LoadColumnWidths()
    {
        DetailsHeader.Children.Clear();
        foreach (var column in DetailsColumn.Defaults())
        {
            var (button, resize) = column.Id switch
            {
                DetailsColumnId.Name => (NameHeader, NameResize),
                DetailsColumnId.Modified => (ModifiedHeader, ModifiedResize),
                DetailsColumnId.Type => (TypeHeader, TypeResize),
                DetailsColumnId.Size => (SizeHeader, SizeResize),
                _ => CreateColumnHeader(column)
            };
            resize.Tag = column.Id;
            _columnHeaders.Add(column.Id, (button, resize));
            DetailsHeader.Children.Add(button);
            DetailsHeader.Children.Add(resize);
            AttachColumnEditing(button, column.Id);
            AutomationProperties.SetName(button, column.Title);
        }
        // Buttons consume some routed right-click events; listen on the full header,
        // including already handled events and the empty area after the last column.
        HeaderScroller.AddHandler(RightTappedEvent, new RightTappedEventHandler((_, e) =>
        {
            ShowColumnMenu(e.GetPosition(DetailsHeader));
            e.Handled = true;
        }), true);
        DetailsHeader.ContextRequested += (_, e) =>
        {
            ShowColumnMenu(e.TryGetPosition(DetailsHeader, out var p) ? p : null);
            e.Handled = true;
        };
        _headerDivider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            Background = ((Border)NameResize.Child).Background };
        DetailsHeader.Children.Add(_headerDivider);
        _columnDropIndicator = new Border { Width = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Background = (Brush)Application.Current.Resources["FilesMate.Selection.AccentBrush"] };
        DetailsHeader.Children.Add(_columnDropIndicator);
        _detailColumns = DetailsColumn.Normalize(LegacyColumns());
        ApplyDetailsColumns(false);
    }

    private (Button, Border) CreateColumnHeader(DetailsColumn column)
    {
        var glyph = new FontIcon { Glyph = "\uE70E", FontSize = 10, Visibility = Visibility.Collapsed,
            RenderTransform = new RotateTransform(), RenderTransformOrigin = new Point(.5, .5) };
        _extraSortGlyphs.Add(column.Id, glyph);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new TextBlock { Text = column.Title });
        content.Children.Add(glyph);
        var button = new Button { Content = content, Style = NameHeader.Style };
        button.Click += (_, _) => RequestColumnSort(column.Sort);
        var resize = new Border { Width = 12, Margin = new Thickness(0, 0, -6, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), ManipulationMode = ManipulationModes.None,
            Child = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Center,
                Background = ((Border)NameResize.Child).Background } };
        resize.PointerEntered += ColumnResize_PointerEntered;
        resize.PointerExited += ColumnResize_PointerExited;
        resize.PointerPressed += ColumnResize_PointerPressed;
        resize.PointerMoved += ColumnResize_PointerMoved;
        resize.PointerReleased += ColumnResize_PointerReleased;
        resize.PointerCaptureLost += ColumnResize_PointerCaptureLost;
        return (button, resize);
    }

    private void ApplyDetailsColumns(bool persist)
    {
        HideColumnDropIndicator();
        var contentWidth = FileColumnLayout.RowWidth(VisibleColumnWidth, 0, 0, 0);
        Repeater.MinWidth = _layout == FileLayoutKind.Details ? contentWidth : 0;
        DetailsHeader.Width = Math.Max(contentWidth, Scroller.ActualWidth);
        DetailsHeader.ColumnDefinitions.Clear();
        DetailsHeader.ColumnDefinitions.Add(new() { Width = new GridLength(FileColumnLayout.AccentWidth) });
        foreach (var column in _detailColumns)
        {
            if (!_columnHeaders.TryGetValue(column.Id, out var controls)) continue;
            var position = DetailsHeader.ColumnDefinitions.Count;
            Grid.SetColumn(controls.Button, position);
            Grid.SetColumnSpan(controls.Button, 1);
            if (column.Id == DetailsColumnId.Name)
            {
                DetailsHeader.ColumnDefinitions.Add(new() { Width = new GridLength(FileColumnLayout.GlyphWidth) });
                Grid.SetColumn(controls.Button, position == 1 ? 0 : position);
                Grid.SetColumnSpan(controls.Button, position == 1 ? 3 : 2);
                controls.Button.Padding = new Thickness((position == 1 ? FileColumnLayout.NameHeaderPad : 28) - controls.Button.Margin.Left, 0, 8, 0);
                position++;
            }
            DetailsHeader.ColumnDefinitions.Add(new() { Width = new GridLength(column.Visible ? column.Width : 0) });
            Grid.SetColumn(controls.Resize, position);
            controls.Button.Visibility = controls.Resize.Visibility = column.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
        DetailsHeader.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (_headerDivider is not null) Grid.SetColumnSpan(_headerDivider, DetailsHeader.ColumnDefinitions.Count);
        foreach (var row in _realized) row.ApplyColumns(_detailColumns);
        if (persist) PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowColumnMenu(Point? position)
    {
        if (_columnMenuOpen) return;
        var menu = new MenuFlyout();
        menu.Closed += (_, _) => _columnMenuOpen = false;
        foreach (var column in _detailColumns)
        {
            var item = new ToggleMenuFlyoutItem { Text = column.Title, IsChecked = column.Visible, IsEnabled = column.Id != DetailsColumnId.Name };
            item.Click += (_, _) =>
            {
                _detailColumns = _detailColumns.Select(c => c.Id == column.Id ? c with { Visible = item.IsChecked } : c).ToArray();
                ApplyDetailsColumns(true);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var reset = new MenuFlyoutItem { Text = Loc.Get("Columns_Reset") };
        reset.Click += (_, _) => { _detailColumns = DetailsColumn.Defaults(); ApplyDetailsColumns(true); };
        menu.Items.Add(reset);
        var options = new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        if (position is { } point) options.Position = point;
        _columnMenuOpen = true;
        try { menu.ShowAt(DetailsHeader, options); }
        catch { _columnMenuOpen = false; throw; }
    }

    private void AttachColumnEditing(Button button, DetailsColumnId id)
    {
        button.CanDrag = true;
        button.AllowDrop = true;
        var pressed = false;
        Point origin = default;
        button.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            pressed = e.GetCurrentPoint(button).Properties.IsLeftButtonPressed;
            origin = e.GetCurrentPoint(button).Position;
            _suppressHeaderClick = false;
        }), true);
        button.AddHandler(PointerMovedEvent, new PointerEventHandler(async (_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            if (!pressed || !point.Properties.IsLeftButtonPressed || Math.Abs(point.Position.X - origin.X) < 8) return;
            pressed = false;
            _suppressHeaderClick = true;
            button.ReleasePointerCaptures();
            try { await button.StartDragAsync(point); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Column drag failed: {0}", error.Message); }
            finally { HideColumnDropIndicator(); }
        }), true);
        button.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => pressed = false), true);
        button.KeyDown += (_, _) => _suppressHeaderClick = false;
        button.DragStarting += (_, e) =>
        {
            _suppressHeaderClick = true;
            e.Data.Properties[ColumnDragFormat] = _columnDragToken;
            e.Data.SetData(ColumnDragFormat, (int)id);
            e.Data.RequestedOperation = e.AllowedOperations = DataPackageOperation.Move;
        };
        button.DragOver += (_, e) =>
        {
            if (!OwnColumnDrag(e)) return;
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            ShowColumnDropIndicator(button, e.GetPosition(button).X >= button.ActualWidth / 2);
            e.Handled = true;
        };
        button.DragLeave += (_, _) => HideColumnDropIndicator();
        button.Drop += async (_, e) =>
        {
            if (!OwnColumnDrag(e)) return;
            e.Handled = true;
            var after = e.GetPosition(button).X >= button.ActualWidth / 2;
            var deferral = e.GetDeferral();
            try
            {
                var movingId = (DetailsColumnId)(int)await e.DataView.GetDataAsync(ColumnDragFormat);
                if (movingId == id) return;
                var moving = _detailColumns.Single(c => c.Id == movingId);
                var ordered = _detailColumns.Where(c => c.Id != movingId).ToList();
                ordered.Insert(ordered.FindIndex(c => c.Id == id) + (after ? 1 : 0), moving);
                _detailColumns = ordered.ToArray();
                ApplyDetailsColumns(true);
            }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Column drop failed: {0}", error.Message); }
            finally { HideColumnDropIndicator(); deferral.Complete(); }
        };
    }

    private void ShowColumnDropIndicator(Button button, bool after)
    {
        if (_columnDropIndicator is not { } indicator) return;
        Grid.SetColumn(indicator, Grid.GetColumn(button));
        Grid.SetColumnSpan(indicator, Grid.GetColumnSpan(button));
        indicator.HorizontalAlignment = after ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        indicator.Visibility = Visibility.Visible;
    }

    private void HideColumnDropIndicator()
    {
        if (_columnDropIndicator is { } indicator) indicator.Visibility = Visibility.Collapsed;
    }

    private bool OwnColumnDrag(DragEventArgs e) => e.DataView.Properties.TryGetValue(ColumnDragFormat, out var token) && Equals(token, _columnDragToken);
    private void UpdateExtraSortGlyphs()
    {
        foreach (var (id, glyph) in _extraSortGlyphs) ApplySortGlyph(glyph, _headerSort, _detailColumns.Single(c => c.Id == id).Sort);
    }
    private void RequestColumnSort(EntrySortColumn column) { if (!_suppressHeaderClick) SortRequested?.Invoke(this, column); }
    private void NameHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Name);
    private void ModifiedHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Modified);
    private void TypeHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Type);
    private void SizeHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Size);
    private void ColumnResize_PointerEntered(object sender, PointerRoutedEventArgs e) => ProtectedCursor = DesktopCursors.SizeWestEast;
    private void ColumnResize_PointerExited(object sender, PointerRoutedEventArgs e) { if (_resizeColumn < 0) ProtectedCursor = null; }
    private void ColumnResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn >= 0 || !e.GetCurrentPoint(DetailsHeader).Properties.IsLeftButtonPressed || !((UIElement)sender).CapturePointer(e.Pointer)) return;
        _resizeColumn = (int)(DetailsColumnId)((FrameworkElement)sender).Tag;
        _resizePointerId = e.Pointer.PointerId;
        _resizeOriginX = e.GetCurrentPoint(DetailsHeader).Position.X;
        _resizeOriginWidth = _detailColumns.Single(c => (int)c.Id == _resizeColumn).Width;
        e.Handled = true;
    }
    private void ColumnResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn < 0 || _resizePointerId != e.Pointer.PointerId) return;
        var width = Math.Clamp(_resizeOriginWidth + e.GetCurrentPoint(DetailsHeader).Position.X - _resizeOriginX, _resizeColumn == 0 ? 96 : 64, 560);
        _detailColumns = _detailColumns.Select(c => (int)c.Id == _resizeColumn ? c with { Width = width } : c).ToArray();
        ApplyDetailsColumns(false);
        e.Handled = true;
    }
    private void ColumnResize_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn < 0 || _resizePointerId != e.Pointer.PointerId) return;
        _resizeColumn = -1;
        _resizePointerId = null;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        ProtectedCursor = null;
        ApplyDetailsColumns(true);
        e.Handled = true;
    }
    private void ColumnResize_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn < 0 || _resizePointerId != e.Pointer.PointerId) return;
        _resizeColumn = -1;
        _resizePointerId = null;
        ProtectedCursor = null;
        ApplyDetailsColumns(true);
    }
}
