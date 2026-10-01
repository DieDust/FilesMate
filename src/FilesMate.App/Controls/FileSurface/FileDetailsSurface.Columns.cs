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
    private readonly Dictionary<string, (Button Button, Border Resize)> _columnHeaders = [];
    private readonly Dictionary<string, FontIcon> _extraSortGlyphs = [];
    private EntrySort _headerSort = EntrySort.Name;
    private bool _suppressHeaderClick;
    private bool _columnMenuOpen;
    private MenuFlyout? _columnMenu;
    private Border? _headerDivider;
    private Border? _columnDropIndicator;
    private double VisibleColumnWidth => _detailColumns.Where(c => c.Visible).Sum(c => c.Width);

    public DetailsColumn[] GetColumns() => _detailColumns.ToArray();

    public void SetColumns(DetailsColumn[]? columns)
    {
        var normalized = DetailsColumn.Normalize(columns ?? LegacyColumns());
        if (_detailColumns.SequenceEqual(normalized)) return;
        _detailColumns = normalized;
        RememberManualNameWidth();
        ApplyDetailsColumns(false);
        ScheduleAutoNameMeasurement();
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
            resize.Tag = column.Key;
            if (column.Id is DetailsColumnId.Name or DetailsColumnId.Modified or DetailsColumnId.Type or DetailsColumnId.Size)
                resize.DoubleTapped += (_, e) => { FitColumn(column.Key); e.Handled = true; };
            _columnHeaders.Add(column.Key, (button, resize));
            DetailsHeader.Children.Add(button);
            DetailsHeader.Children.Add(resize);
            AttachColumnEditing(button, column.Key);
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
        _columnDropIndicator = new Border { Width = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Theming.ThemeResources.Bind(_headerDivider, Border.BackgroundProperty, "FilesMate.Divider.Brush");
        Theming.ThemeResources.Bind(_columnDropIndicator, Border.BackgroundProperty, "FilesMate.Selection.AccentBrush");
        DetailsHeader.Children.Add(_columnDropIndicator);
        _detailColumns = DetailsColumn.Normalize(LegacyColumns());
        RememberManualNameWidth();
        ApplyDetailsColumns(false);
    }

    private (Button, Border) CreateColumnHeader(DetailsColumn column)
    {
        var glyph = new FontIcon { Glyph = "\uE70E", FontSize = 10, Visibility = Visibility.Collapsed,
            RenderTransform = new RotateTransform(), RenderTransformOrigin = new Point(.5, .5) };
        if (column.CanSort) _extraSortGlyphs[column.Key] = glyph;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new TextBlock { Text = column.Title });
        content.Children.Add(glyph);
        var button = new Button { Content = content, Style = NameHeader.Style };
        if (column.CanSort) button.Click += (_, _) => RequestColumnSort(column);
        var resize = new Border { Width = 12, Margin = new Thickness(0, 0, -6, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), ManipulationMode = ManipulationModes.None,
            Child = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Center,
                Background = ((Border)NameResize.Child).Background } };
        Theming.ThemeResources.Bind((Border)resize.Child, Border.BackgroundProperty, "FilesMate.Divider.Brush");
        resize.PointerEntered += ColumnResize_PointerEntered;
        resize.PointerExited += ColumnResize_PointerExited;
        resize.PointerPressed += ColumnResize_PointerPressed;
        resize.PointerMoved += ColumnResize_PointerMoved;
        resize.PointerReleased += ColumnResize_PointerReleased;
        resize.PointerCaptureLost += ColumnResize_PointerCaptureLost;
        resize.DoubleTapped += (_, e) => { FitColumn(column.Key); e.Handled = true; };
        return (button, resize);
    }

    private void ApplyDetailsColumns(bool persist)
    {
        HideColumnDropIndicator();
        foreach (var controls in _columnHeaders.Values)
            controls.Button.Visibility = controls.Resize.Visibility = Visibility.Collapsed;
        var contentWidth = FileColumnLayout.RowWidth(VisibleColumnWidth, 0, 0, 0);
        Repeater.MinWidth = _layout == FileLayoutKind.Details ? contentWidth : 0;
        DetailsHeader.Width = Math.Max(contentWidth, Scroller.ActualWidth);
        DetailsHeader.ColumnDefinitions.Clear();
        DetailsHeader.ColumnDefinitions.Add(new() { Width = new GridLength(FileColumnLayout.AccentWidth) });
        foreach (var column in _detailColumns)
        {
            if (!_columnHeaders.TryGetValue(column.Key, out var controls))
            {
                controls = CreateColumnHeader(column);
                controls.Resize.Tag = column.Key;
                _columnHeaders[column.Key] = controls;
                DetailsHeader.Children.Add(controls.Button);
                DetailsHeader.Children.Add(controls.Resize);
                AttachColumnEditing(controls.Button, column.Key);
                AutomationProperties.SetName(controls.Button, column.Title);
            }
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
            ApplyHeaderTypography(controls.Button);
        }
        DetailsHeader.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (_headerDivider is not null) Grid.SetColumnSpan(_headerDivider, DetailsHeader.ColumnDefinitions.Count);
        foreach (var row in _realized) ApplyRowColumns(row);
        if (persist) PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowColumnMenu(Point? position)
    {
        if (_columnMenuOpen) return;
        var menu = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["FilesMate.ColumnMenuPresenterStyle"],
            ShouldConstrainToRootBounds = true,
            AreOpenCloseAnimationsEnabled = false,
        };
        Theming.FlyoutTheme.FollowHost(menu);
        _columnMenu = menu;
        menu.Closed += (_, _) => { _columnMenuOpen = false; _columnMenu = null; };
        var target = ColumnAt(position?.X);
        var itemStyle = (Style)Application.Current.Resources["FilesMate.ColumnMenuItemStyle"];
        var toggleStyle = (Style)Application.Current.Resources["FilesMate.ColumnMenuToggleStyle"];
        var separatorStyle = (Style)Application.Current.Resources["FilesMate.ColumnMenuSeparatorStyle"];
        var fit = new MenuFlyoutItem { Text = Loc.Get("Columns_Fit"), Style = itemStyle, Icon = WidthGlyph(), IsEnabled = target is not null };
        fit.Click += (_, _) => { if (target is not null) FitColumn(target); };
        menu.Items.Add(fit);
        var fitAll = new MenuFlyoutItem { Text = Loc.Get("Columns_FitAll"), Style = itemStyle, Icon = WidthGlyph() };
        fitAll.Click += (_, _) => FitColumn(null);
        menu.Items.Add(fitAll);
        menu.Items.Add(new MenuFlyoutSeparator { Style = separatorStyle });
        foreach (var column in _detailColumns.Where(c => c.Id != DetailsColumnId.ShellProperty || c.Visible))
        {
            var item = new ToggleMenuFlyoutItem { Text = column.Title, Style = toggleStyle, IsChecked = column.Visible, IsEnabled = column.Id != DetailsColumnId.Name };
            item.Click += (_, _) =>
            {
                _detailColumns = _detailColumns.Select(c => c.Key == column.Key ? c with { Visible = item.IsChecked } : c).ToArray();
                ApplyDetailsColumns(true);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator { Style = separatorStyle });
        var chooseIcon = new SymbolIcon(Symbol.List);
        Theming.ThemeResources.Bind(chooseIcon, IconElement.ForegroundProperty, "FilesMate.Selection.AccentBrush");
        var choose = new MenuFlyoutItem { Text = Loc.Get("Columns_Choose"), Style = itemStyle, Icon = chooseIcon };
        choose.Click += async (_, _) => await ChooseColumnsAsync();
        menu.Items.Add(choose);
        var reset = new MenuFlyoutItem { Text = Loc.Get("Columns_Reset"), Style = itemStyle, Icon = MenuGlyph("\uE72C") };
        reset.Click += (_, _) => { _detailColumns = DetailsColumn.Defaults(); RememberManualNameWidth(); ApplyDetailsColumns(true); ScheduleAutoNameMeasurement(); };
        menu.Items.Add(reset);
        var options = new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        if (position is { } point) options.Position = point;
        _columnMenuOpen = true;
        try { menu.ShowAt(DetailsHeader, options); }
        catch { _columnMenuOpen = false; throw; }
    }

    private static FontIcon MenuGlyph(string glyph)
    {
        var icon = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), Glyph = glyph, FontSize = 16 };
        Theming.ThemeResources.Bind(icon, IconElement.ForegroundProperty, "FilesMate.Selection.AccentBrush");
        return icon;
    }

    private static PathIcon WidthGlyph()
    {
        // Horizontal arrows between column boundaries describe sizing, rather than switching items.
        var icon = (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M0,1 L1,1 L1,15 L0,15 Z M15,1 L16,1 L16,15 L15,15 Z M2,8 L5,5 L5.7,5.7 L3.9,7.5 L12.1,7.5 L10.3,5.7 L11,5 L14,8 L11,11 L10.3,10.3 L12.1,8.5 L3.9,8.5 L5.7,10.3 L5,11 Z' />");
        Theming.ThemeResources.Bind(icon, IconElement.ForegroundProperty, "FilesMate.Selection.AccentBrush");
        return icon;
    }

    private void AttachColumnEditing(Button button, string id)
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
            e.Data.SetData(ColumnDragFormat, id);
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
                var movingId = (string)await e.DataView.GetDataAsync(ColumnDragFormat);
                if (movingId == id) return;
                var moving = _detailColumns.Single(c => c.Key == movingId);
                var ordered = _detailColumns.Where(c => c.Key != movingId).ToList();
                ordered.Insert(ordered.FindIndex(c => c.Key == id) + (after ? 1 : 0), moving);
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
        foreach (var (key, glyph) in _extraSortGlyphs)
        {
            var column = _detailColumns.FirstOrDefault(c => c.Key == key);
            glyph.Visibility = column?.IsSortedBy(_headerSort) == true ? Visibility.Visible : Visibility.Collapsed;
            if (glyph.RenderTransform is RotateTransform rotate) rotate.Angle = _headerSort.Ascending ? 0 : 180;
        }
    }
    public event EventHandler<EntrySort>? SortSpecificationRequested;
    private void RequestColumnSort(DetailsColumn column)
    {
        if (_suppressHeaderClick) return;
        if (column.Id == DetailsColumnId.ShellProperty)
            SortSpecificationRequested?.Invoke(this, _headerSort.SelectColumn(column.Sort,
                App.ExplorerPreferences.DefaultSortAscending, column.PropertyName));
        else RequestColumnSort(column.Sort);
    }
    private void RequestColumnSort(EntrySortColumn column) { if (!_suppressHeaderClick) SortRequested?.Invoke(this, column); }
    private void NameHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Name);
    private void ModifiedHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Modified);
    private void TypeHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Type);
    private void SizeHeader_Click(object sender, RoutedEventArgs e) => RequestColumnSort(EntrySortColumn.Size);
    private void ColumnResize_PointerEntered(object sender, PointerRoutedEventArgs e) => ProtectedCursor = DesktopCursors.SizeWestEast;
    private void ColumnResize_PointerExited(object sender, PointerRoutedEventArgs e) { if (_resizeColumn is null) ProtectedCursor = null; }
    private void ColumnResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn is not null || !e.GetCurrentPoint(DetailsHeader).Properties.IsLeftButtonPressed || !((UIElement)sender).CapturePointer(e.Pointer)) return;
        _resizeColumn = (string)((FrameworkElement)sender).Tag;
        _resizePointerId = e.Pointer.PointerId;
        _resizeOriginX = e.GetCurrentPoint(DetailsHeader).Position.X;
        _resizeOriginWidth = _detailColumns.Single(c => c.Key == _resizeColumn).Width;
        if (_resizeColumn == "Name") CancelAutoNameMeasurement();
        e.Handled = true;
    }
    private void ColumnResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn is null || _resizePointerId != e.Pointer.PointerId) return;
        var width = Math.Clamp(_resizeOriginWidth + e.GetCurrentPoint(DetailsHeader).Position.X - _resizeOriginX, _resizeColumn == "Name" ? 96 : 64, 1200);
        _detailColumns = _detailColumns.Select(c => c.Key == _resizeColumn ? c with { Width = width } : c).ToArray();
        ApplyDetailsColumns(false);
        e.Handled = true;
    }
    private void ColumnResize_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn is null || _resizePointerId != e.Pointer.PointerId) return;
        var resizedName = _resizeColumn == "Name";
        _resizeColumn = null;
        _resizePointerId = null;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        ProtectedCursor = null;
        if (resizedName) RememberManualNameWidth();
        ApplyDetailsColumns(true);
        ScheduleAutoNameMeasurement();
        e.Handled = true;
    }
    private void ColumnResize_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeColumn is null || _resizePointerId != e.Pointer.PointerId) return;
        var resizedName = _resizeColumn == "Name";
        _resizeColumn = null;
        _resizePointerId = null;
        ProtectedCursor = null;
        if (resizedName) RememberManualNameWidth();
        ApplyDetailsColumns(true);
        ScheduleAutoNameMeasurement();
    }
}
