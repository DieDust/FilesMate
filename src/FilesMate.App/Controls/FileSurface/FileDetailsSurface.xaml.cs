using System.Diagnostics;

using FilesMate.App.Animations;
using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Icons;
using FilesMate.App.Input;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Icons;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Shell;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

using WinRT.Interop;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface : UserControl
{
    private readonly EntryItemsSource _items = new();
    private readonly SelectionModel _selection = new();
    private readonly VisibleRangeTracker _tracker = new();
    private readonly NameJumpSession _nameJump = new();
    private readonly Stopwatch _inputClock = Stopwatch.StartNew();
    private readonly HashSet<FileRow> _realized = [];
    private readonly HashSet<FileTile> _tiles = [];
    private readonly Stopwatch _prepareWatch = new();
    private readonly List<int> _marqueeHits = [];
    private readonly StackLayout _stackLayout = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = FileColumnLayout.RowGap,
    };

    private bool _pointerDown;
    private bool _dragging;
    private bool _dragCandidate;
    private bool _externalDragStarted;
    private bool _layoutReady;
    private bool _zoomChangePending;
    private bool _tileMetricsPending;
    private int _pendingZoomDirection;
    private long _pendingZoomGeneration = -1;
    private Point _dragStart;
    private Point _marqueeStart;
    private Point _marqueePointer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _marqueeScrollTimer;
    private int _pressViewIndex = -1;
    private int _lastClickViewIndex = -1;
    private int _dropTargetViewIndex = -1;
    private long _lastClickTimestamp;
    private long _generation;
    private CancellationTokenSource _folderSizeCts = new();
    private IReadOnlyList<string> _dragSourcePaths = [];
    private IReadOnlyList<IStorageItem> _dragStorageItems = [];
    private SoftwareBitmap? _dragPreview;
    private bool _rangePending;
    private int? _revealEntryId;
    private long _revealGeneration;
    private bool _revealScheduled;
    private FileLayoutKind _layout = FileLayoutKind.Grid;
    private GridSizePreset _gridPreset = GridSizePreset.Default;
    private int _resizeColumn = -1;
    private uint? _resizePointerId;
    private double _resizeOriginX;
    private double _resizeOriginWidth;

    public FileDetailsSurface()
    {
        InitializeComponent();
#if FILESMATE_UI_TEST
        if (Environment.GetEnvironmentVariable("FILESMATE_DRAG_PREVIEW_TEST") == "1")
            Loaded += async (_, _) => await FileDragPreview.ExportSmokeAsync(DragPreviewHost);
#endif
        NameHeaderText.Text = StringTable.Get("Column_Name");
        ModifiedHeaderText.Text = StringTable.Get("Column_Modified");
        TypeHeaderText.Text = StringTable.Get("Column_Type");
        SizeHeaderText.Text = StringTable.Get("Column_Size");
        Repeater.ItemsSource = _items;
        LoadColumnWidths();
        InitializeAlphabet();
        Scroller.SizeChanged += (_, _) =>
        {
            UpdateAlphabetTailSpace();
            ApplyDetailsColumns(persist: false);
            ScheduleVisibleRange();
            SchedulePendingReveal();
        };
        Repeater.SizeChanged += (_, _) => SchedulePendingReveal();
        GotFocus += (_, _) => RefreshRealizedSelection();
        LostFocus += (_, _) => RefreshRealizedSelection();
        Unloaded += (_, _) =>
        {
            CancelMarquee();
            CancelFolderHover();
            CancelInlineRename();
            CancelPendingReveal();
            ResetPendingZoom();
            ResetFolderSizeWalks();
        };
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        _layoutReady = true;
    }

    public Func<FileEntryCore, string>? ResolvePath { get; set; }

    public Func<string?>? ResolveFolder { get; set; }

    public Func<string?, bool>? IsPinnedPath { get; set; }

    /// <summary>Resolves tags only for realized/selected entries; never called for the whole store.</summary>
    public Func<FileEntryCore, IReadOnlyList<TagDefinition>>? ResolveTags { get; set; }

    public Func<UIElement?>? CreateTagPicker { get; set; }

    public FileLayoutKind LayoutKind => _layout;

    public GridSizePreset GridPreset => _gridPreset;
    public double ScrollOffset => Scroller.VerticalOffset;
    internal bool IsBoundTo(EntryStore? store, EntryViewIndex index) => ReferenceEquals(_items.Store, store) && ReferenceEquals(_items.Index, index);
    public void RestoreScrollOffset(double offset)
    {
        if (!double.IsFinite(offset)) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!IsLoaded) return;
            UpdateLayout();
            Scroller.ChangeView(null, Math.Clamp(offset, 0, Scroller.ScrollableHeight), null, true);
        });
    }

    public event EventHandler<FileEntryCore>? OpenRequested;

    public event EventHandler<FileEntryCore>? OpenInNewTabRequested;

    public event EventHandler? UpRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? ForwardRequested;

    public event EventHandler? CopyPathRequested;
    public event EventHandler? QuickPreviewRequested;
    public Func<string?>? OtherPanePath { get; set; }
    public void SelectSameType() => ChangeSelection(s => s.SelectSameType(_items.Store!, _items.Index!));
    public void InvertSelection() => ChangeSelection(s => s.Invert(_items.Store!, _items.Index!));
    public void RestoreSelectedNames(IReadOnlyList<string> names)
    {
        if (_items.Store is null || _items.Index is null) return;
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var views = Enumerable.Range(0, _items.Count).Where(i => _items.TryGetEntry(i, out var entry) && wanted.Contains(entry.Name)).ToArray();
        ChangeSelection(s => s.ReplaceFromViewIndices(_items.Store, _items.Index, views));
    }
    private void ChangeSelection(Action<SelectionModel> change)
    {
        if (_items.Store is null || _items.Index is null) return;
        change(_selection);
        RefreshRealizedSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler<EntrySortColumn>? SortRequested;

    public event EventHandler? SelectionChanged;

    public Func<FileDropRequest, Task>? DropRequested { get; set; }

    public event EventHandler? PresentationChanged;

    public event EventHandler<AppCommandId>? CommandRequested;
    public event EventHandler<string>? TerminalRequested;

    public bool ClipboardHasFiles { get; set; }

    public bool IsFolderWritable { get; set; } = true;

    public SelectionModel Selection => _selection;
    public bool IsMarqueeSelecting => _dragging;

    public IReadOnlyList<string> SelectedPaths() => ResolveSelectionPaths();

    public long SelectedFileBytes()
    {
        var store = _items.Store;
        var index = _items.Index;
        if (store is null || index is null || _selection.Count == 0)
        {
            return 0;
        }

        long total = 0;
        // ponytail: one pass over the current view; upgrade if select-all on huge folders shows up in traces
        for (var i = 0; i < index.Count; i++)
        {
            var entry = store[index[i]];
            if (!_selection.Contains(entry.Id))
            {
                continue;
            }

            long size;
            if (entry.Kind == EntryKind.Directory)
            {
                if (!App.ExplorerPreferences.ShowFolderSizes
                    || ResolvePath?.Invoke(entry) is not { Length: > 0 } path
                    || !FolderSizeCache.TryGet(path, out var folderBytes))
                {
                    continue;
                }

                size = folderBytes > long.MaxValue ? long.MaxValue : (long)folderBytes;
            }
            else
            {
                size = entry.Size > long.MaxValue ? long.MaxValue : (long)entry.Size;
            }
            if (total > long.MaxValue - size)
            {
                return long.MaxValue;
            }

            total += size;
        }

        return total;
    }

    public string? PrimaryPath() => TryGetPrimaryPath();

    public EntryItemsSource Items => _items;

    public bool CanRefresh { get; set; } = true;

    public int RealizedCount => _realized.Count;

    public VisibleRange VisibleRange { get; private set; }

    public TimeSpan LastPrepareDuration { get; private set; }

    public void CancelFolderSizeWalks()
    {
        ResetFolderSizeWalks();
        _dragStorageItems = [];
        _dragSourcePaths = [];
    }

    public void ReleaseResources()
    {
        CancelMarquee();
        CancelPendingReveal();
        ResetPendingZoom();
        _dragStorageItems = [];
        _dragSourcePaths = [];
        foreach (var row in _realized.ToArray())
        {
            row.Clear();
        }

        foreach (var tile in _tiles.ToArray())
        {
            tile.Clear();
        }

        _realized.Clear();
        _tiles.Clear();
        _selection.Clear();
        _items.ClearView();
        RefreshAlphabet();
    }

    public void RefreshRealizedTags()
    {
        foreach (var row in _realized.ToArray())
        {
            if (row.EntryId >= 0)
            {
                row.SetTags(ResolveTags?.Invoke(row.Entry));
            }
        }

        foreach (var tile in _tiles.ToArray())
        {
            if (tile.EntryId >= 0)
            {
                tile.SetTags(ResolveTags?.Invoke(tile.Entry));
            }
        }
    }

    public void RefreshRealizedTags(int entryId)
    {
        foreach (var row in _realized.ToArray())
        {
            if (row.EntryId == entryId)
            {
                row.SetTags(ResolveTags?.Invoke(row.Entry));
            }
        }

        foreach (var tile in _tiles.ToArray())
        {
            if (tile.EntryId == entryId)
            {
                tile.SetTags(ResolveTags?.Invoke(tile.Entry));
            }
        }
    }

    public void Bind(EntryStore? store, EntryViewIndex? index, long generation)
    {
        var navigated = generation != _generation;
        if (navigated || !ReferenceEquals(store, _items.Store) || !ReferenceEquals(index, _items.Index))
            CancelMarquee();
        _generation = generation;
        if (navigated)
        {
            if (IsRenaming) CancelInlineRename();
            _nameJump.Reset();
            CancelPendingReveal();
            ResetFolderSizeWalks();
        }
        var selectionChanged = false;
        if (store is null || index is null)
        {
            var hadPublishedView = _items.Store is not null || _items.Index is not null;
            if (hadPublishedView)
            {
                _items.ClearView();
        RefreshAlphabet();
            }

            if (navigated || hadPublishedView)
            {
                _selection.Clear();
                ClearDropTarget();
                _realized.Clear();
                _tiles.Clear();
                _ = Scroller.ChangeView(null, 0, null, disableAnimation: true);
                selectionChanged = true;
            }

            if (selectionChanged)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (navigated)
        {
            ResetPendingZoom();
            _selection.Clear();
            ClearDropTarget();
            _ = Scroller.ChangeView(null, 0, null, disableAnimation: true);
            selectionChanged = true;
        }

        if (!_items.TryPublish(store, index, generation))
        {
            if (selectionChanged)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        selectionChanged |= _selection.RemoveMissing(store);
        // Existing selected files may have changed size/name in a watcher update.
        selectionChanged |= _selection.Count > 0;
        RefreshAlphabet();
        RefreshRealizedSelection();
        UpdateVisibleRange();
        if (selectionChanged)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResetFolderSizeWalks()
    {
        _folderSizeCts.Cancel();
        _folderSizeCts.Dispose();
        _folderSizeCts = new CancellationTokenSource();
    }

    public void SetSort(EntrySort sort)
    {
        _headerSort = sort;
        UpdateExtraSortGlyphs();
        ApplySortGlyph(NameSortGlyph, sort, EntrySortColumn.Name);
        ApplySortGlyph(ModifiedSortGlyph, sort, EntrySortColumn.Modified);
        ApplySortGlyph(TypeSortGlyph, sort, EntrySortColumn.Type);
        ApplySortGlyph(SizeSortGlyph, sort, EntrySortColumn.Size);
    }

    public void SetLayout(FileLayoutKind kind)
    {
        if (_layoutReady && _layout == kind)
        {
            return;
        }

        var switched = _layoutReady && _layout != kind;
        if (switched) CancelMarquee();
        _layout = kind;
        var grid = kind == FileLayoutKind.Grid;
        HeaderRow.Height = grid ? new GridLength(0) : new GridLength(FileColumnLayout.RowHeight);
        DetailsHeader.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        Scroller.HorizontalScrollBarVisibility = grid ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ApplyDetailsColumns(persist: false);
        if (grid)
        {
            _ = Scroller.ChangeView(0, null, null, disableAnimation: true);
        }
        if (!_layoutReady)
        {
            _layoutReady = true;
            ApplyGridMetrics();
            return;
        }

        try
        {
            Repeater.ItemsSource = null;
            Repeater.Layout = grid ? FileGridLayout : _stackLayout;
            Repeater.ItemTemplate = (DataTemplate)Resources[grid ? "TileTemplate" : "RowTemplate"];
            Repeater.ItemsSource = _items;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Layout switch failed: {0}", error);
            try
            {
                Repeater.ItemsSource = _items;
            }
            catch
            {
                // Restoring the source is best-effort after a failed template swap.
            }

            return;
        }

        ApplyGridMetrics();
        UpdateVisibleRange();
        if (grid)
        {
            ScheduleTileMetricsRefresh();
        }

        if (switched)
        {
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }

        _layoutReady = true;
    }

    public void RebindVisibleEntries()
    {
        foreach (var row in _realized.ToArray())
        {
            if (row.ViewIndex >= 0 && _items.TryGetEntry(row.ViewIndex, out var entry))
            {
                row.Bind(row.ViewIndex, entry, _selection.Contains(entry.Id), ResolvePath?.Invoke(entry), _folderSizeCts.Token);
                row.ApplyColumns(_detailColumns);
                row.SetTags(ResolveTags?.Invoke(entry));
            }
        }

        foreach (var tile in _tiles.ToArray())
        {
            if (tile.ViewIndex >= 0 && _items.TryGetEntry(tile.ViewIndex, out var entry))
            {
                tile.Bind(tile.ViewIndex, entry, _selection.Contains(entry.Id), ResolvePath?.Invoke(entry));
                tile.SetTags(ResolveTags?.Invoke(entry));
            }
        }
    }

    public void SetGridSize(GridSizePreset preset)
    {
        var changed = _gridPreset != preset;
        _gridPreset = preset;
        ApplyGridMetrics();
        ScheduleTileMetricsRefresh();
        UpdateVisibleRange();
        if (changed)
        {
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void ApplySortGlyph(FontIcon glyph, EntrySort sort, EntrySortColumn column)
    {
        if (sort.Column != column)
        {
            glyph.Visibility = Visibility.Collapsed;
            if (glyph.RenderTransform is RotateTransform idle)
            {
                idle.Angle = 0;
            }

            return;
        }

        glyph.Visibility = Visibility.Visible;
        glyph.Glyph = "\uE70E";
        RotateSortGlyph(glyph, sort.Ascending);
    }

    private static void RotateSortGlyph(FontIcon glyph, bool ascending)
    {
        var transform = glyph.RenderTransform as RotateTransform ?? new RotateTransform();
        glyph.RenderTransform = transform;
        var to = ascending ? 0d : 180d;
        var duration = App.Motion.Resolve(MotionDurations.Fast);
        if (duration <= TimeSpan.Zero)
        {
            transform.Angle = to;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, nameof(RotateTransform.Angle));
        var board = new Storyboard();
        board.Children.Add(animation);
        board.Begin();
    }

    public void ScrollPrimaryIntoView()
    {
        CancelPendingReveal();
        _revealEntryId = _selection.PrimaryId;
        if (_revealEntryId is null) return;
        _revealGeneration = _generation;
        Repeater.InvalidateMeasure();
        SchedulePendingReveal();
    }

    private void CancelPendingReveal()
    {
        _revealEntryId = null;
    }

    private void SchedulePendingReveal()
    {
        if (_revealEntryId is null || _revealScheduled) return;
        _revealScheduled = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _revealScheduled = false;
            RevealAfterLayout();
        })) _revealScheduled = false;
    }

    private void RevealAfterLayout()
    {
        if (_revealEntryId is null) return;
        if (_revealGeneration != _generation || _revealEntryId != _selection.PrimaryId || !IsLoaded)
        {
            CancelPendingReveal();
            return;
        }

        if (_items.Store is null || _items.Index is null)
        {
            return;
        }

        var view = _selection.ViewIndexOfPrimary(_items.Store, _items.Index);
        if (view < 0)
        {
            CancelPendingReveal();
            return;
        }

        var offset = _layout == FileLayoutKind.Grid
            ? (view / Columns()) * ItemHeight()
            : view * ItemHeight();
        var contentHeight = _layout == FileLayoutKind.Grid ? _gridPreset.ItemHeight : FileColumnLayout.RowHeight;
        if (Scroller.ViewportHeight <= 0 || Scroller.ExtentHeight + 1 < offset + contentHeight)
            return;

        var desired = Math.Clamp(offset, 0, Scroller.ScrollableHeight);
        if (Math.Abs(Scroller.VerticalOffset - desired) < 1
            || Scroller.ChangeView(null, desired, null, disableAnimation: true))
        {
            var selected = _revealEntryId;
            CancelPendingReveal();
            _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (IsLoaded && _selection.PrimaryId == selected) FocusPrimary();
            });
        }
    }

    public bool TrySelectByName(string fileName)
    {
        if (_items.Store is null || _items.Index is null || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (_items.TryFindEntry(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase), out var entry))
        {
            _selection.SelectOnly(entry.Id);
            RefreshRealizedSelection();
            ScrollPrimaryIntoView();
            FocusPrimary();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    public bool TrySelectByPath(string path)
    {
        if (_items.Store is null || _items.Index is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = path.Trim().Trim('"');
        if (_items.TryFindEntry(entry => string.Equals(ResolvePath?.Invoke(entry), full, StringComparison.OrdinalIgnoreCase), out var entry))
        {
            _selection.SelectOnly(entry.Id);
            RefreshRealizedSelection();
            ScrollPrimaryIntoView();
            FocusPrimary();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    private void FocusPrimary()
    {
        if (_items.Store is null || _items.Index is null)
        {
            Focus(FocusState.Programmatic);
            return;
        }

        var view = _selection.ViewIndexOfPrimary(_items.Store, _items.Index);
        foreach (var row in _realized)
        {
            if (row.ViewIndex == view)
            {
                _ = row.Focus(FocusState.Programmatic);
                return;
            }
        }

        foreach (var tile in _tiles)
        {
            if (tile.ViewIndex == view)
            {
                _ = tile.Focus(FocusState.Programmatic);
                return;
            }
        }

        Focus(FocusState.Programmatic);
    }

    private void Repeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        _prepareWatch.Restart();
        if (!_items.TryGetEntry(args.Index, out var entry))
        {
            if (args.Element is FileRow emptyRow)
            {
                emptyRow.Clear();
            }
            else if (args.Element is FileTile emptyTile)
            {
                emptyTile.Clear();
            }

            return;
        }

        var path = ResolvePath?.Invoke(entry);
        if (args.Element is FileRow row)
        {
            row.Bind(args.Index, entry, _selection.Contains(entry.Id), path, _folderSizeCts.Token);
            row.ApplyColumns(_detailColumns);
            row.SetTags(ResolveTags?.Invoke(entry));
            row.SetDropTarget(args.Index == _dropTargetViewIndex);
            _realized.Add(row);
        }
        else if (args.Element is FileTile tile)
        {
            tile.ApplyMetrics(_gridPreset, GridItemWidth());
            tile.Bind(args.Index, entry, _selection.Contains(entry.Id), path);
            tile.SetTags(ResolveTags?.Invoke(entry));
            tile.SetDropTarget(args.Index == _dropTargetViewIndex);
            _tiles.Add(tile);
        }

        _prepareWatch.Stop();
        LastPrepareDuration = _prepareWatch.Elapsed;
    }

    private void Repeater_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (ReferenceEquals(args.Element, _renameRow)) CancelInlineRename();
        if (args.Element is FileRow row)
        {
            row.ResetVisual();
            row.Clear();
            _realized.Remove(row);
        }
        else if (args.Element is FileTile tile)
        {
            tile.ResetVisual();
            tile.Clear();
            _tiles.Remove(tile);
        }
    }

    private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateMarquee();
        UpdateAlphabetTailSpace();
        if (_layout == FileLayoutKind.Details)
        {
            _ = HeaderScroller.ChangeView(Scroller.HorizontalOffset, null, null, disableAnimation: true);
        }

        UpdateAlphabetPosition(AlphabetLetters.Visibility == Visibility.Visible);
        ScheduleVisibleRange();
    }

    private void ScheduleVisibleRange()
    {
        if (_rangePending)
        {
            return;
        }

        _rangePending = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyPendingVisibleRange))
        {
            _rangePending = false;
        }
    }

    private void ApplyPendingVisibleRange()
    {
        _rangePending = false;
        if (IsLoaded)
        {
            UpdateVisibleRange();
        }
    }

    private void UpdateVisibleRange()
    {
        VisibleRange = _tracker.Update(
            Scroller.VerticalOffset,
            Scroller.ViewportHeight,
            ItemHeight(),
            _layout == FileLayoutKind.Grid
                ? Math.Max(1, (int)Math.Ceiling(_items.Count / (double)Columns()))
                : _items.Count);
    }

    private void RefreshRealizedSelection()
    {
        var primary = _items.Store is null || _items.Index is null
            ? -1
            : _selection.ViewIndexOfPrimary(_items.Store, _items.Index);
        var focused = FocusState != FocusState.Unfocused;
        foreach (var row in _realized.ToArray())
        {
            if (row.EntryId >= 0)
            {
                row.SetSelected(_selection.Contains(row.EntryId));
                row.SetFocused(focused && row.ViewIndex == primary);
                row.SetDropTarget(row.ViewIndex == _dropTargetViewIndex);
            }
        }

        foreach (var tile in _tiles.ToArray())
        {
            if (tile.EntryId >= 0)
            {
                tile.SetSelected(_selection.Contains(tile.EntryId));
                tile.SetFocused(focused && tile.ViewIndex == primary);
                tile.SetDropTarget(tile.ViewIndex == _dropTargetViewIndex);
            }
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsRenaming) return;
        _nameJump.Reset();
        var point = e.GetCurrentPoint(Scroller);
        if (_resizeColumn >= 0 || point.Position.Y < 0 || point.Position.Y >= Scroller.ActualHeight)
        {
            return;
        }

        Focus(FocusState.Programmatic);
        if (point.Properties.PointerUpdateKind is PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed)
        {
            if (point.Properties.PointerUpdateKind == PointerUpdateKind.XButton1Pressed) BackRequested?.Invoke(this, EventArgs.Empty);
            else ForwardRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed)
        {
            var index = ViewIndexFromPoint(point.Position.X, point.Position.Y + Scroller.VerticalOffset);
            if (_items.TryGetEntry(index, out var folder) && folder.Kind == EntryKind.Directory)
                OpenInNewTabRequested?.Invoke(this, folder);
            e.Handled = true;
            return;
        }
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            _lastClickViewIndex = -1;
            return;
        }

        _pointerDown = true;
        _dragging = false;
        _dragCandidate = false;
        _externalDragStarted = false;
        _dragStart = point.Position;
        // Keep the anchor in content space: scrolling must not move the first selected row.
        _marqueeStart = ToContentPoint(point.Position);
        _marqueePointer = point.Position;
        _pressViewIndex = ViewIndexFromPoint(point.Position.X, point.Position.Y + Scroller.VerticalOffset);
        CapturePointer(e.Pointer);

        if (_pressViewIndex < 0 || !_items.TryGetEntry(_pressViewIndex, out var entry))
        {
            if (!IsModifier(VirtualKey.Control) && !IsModifier(VirtualKey.Shift))
            {
                _selection.Clear();
                RefreshRealizedSelection();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            e.Handled = true;
            return;
        }

        var ctrl = IsModifier(VirtualKey.Control);
        var shift = IsModifier(VirtualKey.Shift);
        if (shift && _items.Store is not null && _items.Index is not null)
        {
            var anchor = _selection.AnchorId is int aid
                ? _items.Index.IndexOfId(_items.Store, aid)
                : _pressViewIndex;
            _selection.SelectRange(_items.Store, _items.Index, anchor, _pressViewIndex);
        }
        else if (ctrl)
        {
            _selection.Toggle(entry.Id);
        }
        else if (!_selection.Contains(entry.Id))
        {
            _selection.SelectOnly(entry.Id);
        }

        _dragCandidate = true;

        RefreshRealizedSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        if (!ctrl && !shift && IsDoubleClick(_pressViewIndex))
        {
            OpenRequested?.Invoke(this, entry);
            _lastClickViewIndex = -1;
        }

        e.Handled = true;
    }

    private bool IsDoubleClick(int viewIndex)
    {
        var now = Stopwatch.GetTimestamp();
        var repeat = viewIndex == _lastClickViewIndex
            && viewIndex >= 0
            && Stopwatch.GetElapsedTime(_lastClickTimestamp, now).TotalMilliseconds <= 500;
        _lastClickViewIndex = viewIndex;
        _lastClickTimestamp = now;
        return repeat;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerDown)
        {
            return;
        }

        var point = e.GetCurrentPoint(Scroller);
        var pos = point.Position;
        _marqueePointer = pos;
        if (!_dragging && !_externalDragStarted && Hypot(pos.X - _dragStart.X, pos.Y - _dragStart.Y) > 4)
        {
            if (_dragCandidate)
            {
                _externalDragStarted = true;
                _dragCandidate = false;
                _pointerDown = false;
                _dragSourcePaths = ResolveSelectionPaths(viewOrder: true);
                SetDragState(true);
                _ = BeginExternalDragAsync(e);
                e.Handled = true;
                return;
            }

            _dragging = true;
            _lastClickViewIndex = -1;
            Marquee.Visibility = Visibility.Visible;
            StartMarqueeAutoScroll();
        }

        if (!_dragging || _items.Store is null || _items.Index is null)
        {
            return;
        }

        UpdateMarquee();
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _marqueePointer = e.GetCurrentPoint(Scroller).Position;
        UpdateMarquee();
        _marqueeScrollTimer?.Stop();
        _pointerDown = false;
        _dragCandidate = false;
        Marquee.Visibility = Visibility.Collapsed;
        // Releasing capture synchronously raises CaptureLost, which clears _dragging.
        if (_dragging)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        _dragging = false;
        ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _marqueeScrollTimer?.Stop();
        var wasDragging = _dragging;
        _pointerDown = false;
        _dragging = false;
        _dragCandidate = false;
        if (!_externalDragStarted)
        {
            _dragSourcePaths = [];
        }
        Marquee.Visibility = Visibility.Collapsed;
        if (wasDragging) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task BeginExternalDragAsync(PointerRoutedEventArgs e)
    {
        ReleasePointerCaptures();
        var pointer = e.GetCurrentPoint(this);
        try
        {
            if (_dragSourcePaths.Count == 0)
            {
                return;
            }

            _dragStorageItems = await ResolveStorageItemsAsync(_dragSourcePaths);
            _dragPreview = await CreateDragPreviewAsync(_dragSourcePaths);
            await StartDragAsync(pointer);
        }
        catch (OperationCanceledException)
        {
            // A drag can be cancelled by Windows or by another input owner.
        }
        catch (Exception error)
        {
            App.LogFailure("DragStart", error);
        }
        finally
        {
            _externalDragStarted = false;
            _pointerDown = false;
            _dragCandidate = false;
            _dragSourcePaths = [];
            _dragStorageItems = [];
            _dragPreview?.Dispose();
            _dragPreview = null;
            ReleasePointerCaptures();
            SetDragState(false);
            RefreshRealizedSelection();
        }
    }

    private async Task<SoftwareBitmap?> CreateDragPreviewAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        try
        {
            List<ImageSource?> icons = [];
            foreach (var path in DragPreviewSelection.Paths(paths))
            {
                try { icons.Add(await FileDragPreview.LoadIconAsync(path, Directory.Exists(path), _gridPreset.IconSize, XamlRoot)); }
                catch (Exception error) { App.LogFailure("DragItemIcon", error); icons.Add(null); }
            }
            return await FileDragPreview.RenderAsync(DragPreviewHost, icons, paths.Count, _gridPreset.IconSize);
        }
        catch (Exception error)
        {
            App.LogFailure("DragPreview", error);
            return null;
        }
    }

    private void OnDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (e.Data.Properties.ContainsKey(ColumnDragFormat)) return;
        try
        {
            if (_dragStorageItems.Count > 0)
            {
                e.Data.SetStorageItems(_dragStorageItems);
                FileDropRequest.SetSourcePaths(e.Data, _dragSourcePaths);
            }
            else if (_dragSourcePaths.Count > 0)
            {
                e.Data.SetText(string.Join(Environment.NewLine, _dragSourcePaths));
            }

            e.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Move;
            if (_dragPreview is not null)
            {
                e.DragUI.SetContentFromSoftwareBitmap(_dragPreview, new Point(-6, -10));
            }
        }
        catch
        {
            // Drag payload setup must not become an unhandled dispatcher exception.
        }
    }

    private void OnDropCompleted(UIElement sender, DropCompletedEventArgs e)
    {
        CancelFolderHover();
        _externalDragStarted = false;
        _dragSourcePaths = [];
        _dragStorageItems = [];
        _dragPreview?.Dispose();
        _dragPreview = null;
        ReleasePointerCaptures();
        SetDragState(false);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsCaptionVisible = true;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            ClearDropTarget();
            return;
        }

        var point = e.GetPosition(Scroller);
        var candidate = ViewIndexFromPoint(point.X, point.Y + Scroller.VerticalOffset);
        var overItem = candidate >= 0 && _items.TryGetEntry(candidate, out _);
        var overFolder = overItem && _items.TryGetEntry(candidate, out var entry) && entry.Kind == EntryKind.Directory;
        _dropTargetViewIndex = overFolder ? candidate : -1;
        var destination = overFolder ? ResolveDropTargetDirectory() : ResolveFolder?.Invoke();
        var operation = FileDropPolicy.ResolveOperation(
            FileDropRequest.SourcePaths(e.DataView), destination, !overItem || overFolder,
            e.Modifiers.HasFlag(DragDropModifiers.Control), e.Modifiers.HasFlag(DragDropModifiers.Shift));
        e.AcceptedOperation = ToDataOperation(operation);
        if (operation == FileDropOperation.None) _dropTargetViewIndex = -1;
        UpdateFolderHover(operation != FileDropOperation.None && overFolder ? destination : null);
        e.DragUIOverride.IsCaptionVisible = operation != FileDropOperation.None;
        if (operation != FileDropOperation.None)
            e.DragUIOverride.Caption = StringTable.Get(operation == FileDropOperation.Move ? "Drag_MoveTo" : "Drag_CopyTo");
        RefreshRealizedSelection();
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ClearDropTarget();

    private async void OnDrop(object sender, DragEventArgs e)
    {
        CancelFolderHover();
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            OnDragOver(sender, e);
            CancelFolderHover();
            if (e.AcceptedOperation == DataPackageOperation.None) return;
            var destination = ResolveDropTargetDirectory() ?? ResolveFolder?.Invoke();
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (paths.Length > 0)
            {
                var operation = ToDataOperation(FileDropPolicy.ResolveOperation(paths, destination, true,
                    e.Modifiers.HasFlag(DragDropModifiers.Control), e.Modifiers.HasFlag(DragDropModifiers.Shift)));
                e.AcceptedOperation = operation;
                if (operation == DataPackageOperation.None) return;
                if (DropRequested is { } performDrop)
                    await performDrop(new FileDropRequest(paths, destination, operation,
                        e.DataView.Properties.ContainsKey(FileDropRequest.ShelfMarker),
                        e.Modifiers.HasFlag(DragDropModifiers.Control)));
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Drop data retrieval failed: {0}", error);
        }
        finally
        {
            ClearDropTarget();
            deferral.Complete();
        }
    }

    private static DataPackageOperation ToDataOperation(FileDropOperation operation) => operation switch
    {
        FileDropOperation.Copy => DataPackageOperation.Copy,
        FileDropOperation.Move => DataPackageOperation.Move,
        _ => DataPackageOperation.None,
    };

    private string? ResolveDropTargetDirectory()
    {
        if (_dropTargetViewIndex < 0 || !_items.TryGetEntry(_dropTargetViewIndex, out var entry))
        {
            return null;
        }

        return entry.Kind == EntryKind.Directory
            ? ResolvePath?.Invoke(entry)
            : null;
    }

    private IReadOnlyList<string> ResolveSelectionPaths(bool viewOrder = false)
    {
        if (_items.Store is null || _items.Index is null)
        {
            return [];
        }

        var paths = new List<string>(_selection.Count);
        IEnumerable<int> ids = viewOrder
            ? _selection.Ids.OrderBy(id => _items.Index.IndexOfId(_items.Store, id))
            : _selection.Ids;
        foreach (var id in ids)
        {
            var viewIndex = _items.Index.IndexOfId(_items.Store, id);
            if (viewIndex < 0 || !_items.TryGetEntry(viewIndex, out var entry))
            {
                continue;
            }

            var path = ResolvePath?.Invoke(entry);
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static async Task<IReadOnlyList<IStorageItem>> ResolveStorageItemsAsync(
        IReadOnlyList<string> paths)
    {
        var items = new List<IStorageItem>(paths.Count);
        foreach (var path in paths)
        {
            try
            {
                items.Add(Directory.Exists(path)
                    ? await StorageFolder.GetFolderFromPathAsync(path)
                    : await StorageFile.GetFileFromPathAsync(path));
            }
            catch
            {
                // Keep valid items; a text payload remains available as a fallback.
            }
        }

        return items;
    }

    private void ClearDropTarget()
    {
        CancelFolderHover();
        if (_dropTargetViewIndex < 0)
        {
            return;
        }

        _dropTargetViewIndex = -1;
        RefreshRealizedSelection();
    }

    private void SetDragState(bool dragging)
    {
        foreach (var row in _realized.ToArray())
        {
            row.SetDragging(dragging && _selection.Contains(row.EntryId));
        }

        foreach (var tile in _tiles.ToArray())
        {
            tile.SetDragging(dragging && _selection.Contains(tile.EntryId));
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var point = e.GetPosition(Scroller);
        if (point.Y < 0) return;
        var view = ViewIndexFromPoint(point.X, point.Y + Scroller.VerticalOffset);
        var background = true;
        if (view >= 0 && _items.TryGetEntry(view, out var entry))
        {
            background = false;
            if (ContextMenuSelectionPolicy.ShouldReplaceSelection(true, _selection.Contains(entry.Id)))
            {
                _selection.SelectOnly(entry.Id);
                RefreshRealizedSelection();

            }
        }
        else if (ContextMenuSelectionPolicy.ClearsSelectionOnBackground)
        {
            _selection.Clear();
            RefreshRealizedSelection();

        }

        SelectionChanged?.Invoke(this, ContextSelectionChangedEventArgs.Instance);
        e.Handled = true;
        var position = e.GetPosition(this);
        var preferShell = IsModifier(VirtualKey.Shift);
        _ = DispatcherQueue.TryEnqueue(() => ShowContextMenu(background, position, preferShell));
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.Character == ' ' || IsRenaming) return;
        if (IsModifier(VirtualKey.Control) || IsModifier(VirtualKey.Menu) || _items.Store is null || _items.Index is null)
            return;
        var current = _selection.ViewIndexOfPrimary(_items.Store, _items.Index);
        var found = _nameJump.Find(e.Character, _inputClock.Elapsed, current, _items.Count,
            index => _items.TryGetEntry(index, out var entry) ? entry.Name : string.Empty);
        if (found >= 0) MoveTo(found, extend: false);
        if (!char.IsControl(e.Character)) e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || IsRenaming || FocusManager.GetFocusedElement(XamlRoot) is TextBox) return;
        // Alt navigation belongs to the page accelerators, not grid selection.
        if (IsModifier(VirtualKey.Menu))
        {
            if (e.Key == VirtualKey.Up)
            {
                UpRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            return;
        }
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
            or VirtualKey.Home or VirtualKey.End or VirtualKey.Back or VirtualKey.Escape)
            _nameJump.Reset();
        if (_items.Store is null || _items.Index is null || _items.Count == 0)
        {
            if (e.Key == VirtualKey.Back)
            {
                BackRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }

            return;
        }

        var ctrl = IsModifier(VirtualKey.Control);
        var shift = IsModifier(VirtualKey.Shift);
        var current = _selection.ViewIndexOfPrimary(_items.Store, _items.Index);

        switch (e.Key)
        {
            case VirtualKey.Space when !ctrl && !shift:
                QuickPreviewRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.A when ctrl:
                _selection.SelectAll(_items.Store, _items.Index);
                RefreshRealizedSelection();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.Application:
            case VirtualKey.F10 when shift:
                e.Handled = true;
                _ = DispatcherQueue.TryEnqueue(() =>
                    ShowContextMenu(_selection.Count == 0, new Point(ActualWidth / 2, 40)));
                break;
            case VirtualKey.Enter:
                if (_items.TryGetEntry(Math.Max(0, current), out var open))
                {
                    OpenRequested?.Invoke(this, open);
                }

                e.Handled = true;
                break;
            case VirtualKey.Back:
                BackRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                MoveTo(0, shift);
                e.Handled = true;
                break;
            case VirtualKey.End:
                MoveTo(_items.Count - 1, shift);
                e.Handled = true;
                break;
            case VirtualKey.Left:
                MoveTo(Math.Max(0, current - 1), shift);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                MoveTo(Math.Min(_items.Count - 1, current + 1), shift);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                MoveTo(Math.Max(0, current - Columns()), shift);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                MoveTo(current < 0 ? 0 : Math.Min(_items.Count - 1, current + Columns()), shift);
                e.Handled = true;
                break;
            case VirtualKey.PageUp:
                MoveTo(Math.Max(0, current - PageSize()), shift);
                e.Handled = true;
                break;
            case VirtualKey.PageDown:
                MoveTo(Math.Min(_items.Count - 1, current + PageSize()), shift);
                e.Handled = true;
                break;
        }
    }

    private void MoveTo(int viewIndex, bool extend)
    {
        if (_items.Store is null || _items.Index is null || (uint)viewIndex >= (uint)_items.Count)
        {
            return;
        }

        if (extend)
        {
            var anchor = _selection.AnchorId is int aid
                ? _items.Index.IndexOfId(_items.Store, aid)
                : viewIndex;
            _selection.SelectRange(_items.Store, _items.Index, anchor, viewIndex);
        }
        else if (_items.TryGetEntry(viewIndex, out var entry))
        {
            _selection.SelectOnly(entry.Id);
        }

        RefreshRealizedSelection();
        ScrollPrimaryIntoView();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MovePreviewSelection(int step)
    {
        if (_items.Store is null || _items.Index is null || _items.Count == 0) return;
        var current = _selection.ViewIndexOfPrimary(_items.Store, _items.Index);
        MoveTo(Math.Clamp(current + Math.Sign(step), 0, _items.Count - 1), false);
    }

    private int PageSize()
    {
        var n = (int)(Scroller.ViewportHeight / ItemHeight());
        return Math.Max(1, n - 1) * Columns();
    }

    private double ItemHeight() =>
        _layout == FileLayoutKind.Grid
            ? _gridPreset.ItemHeight + _gridPreset.Gutter
            : FileColumnLayout.RowStride;

    private int Columns()
    {
        if (_layout != FileLayoutKind.Grid)
        {
            return 1;
        }

        return GridSizePreset.Columns(ViewportWidth(), _gridPreset);
    }

    private double ViewportWidth()
    {
        var width = Scroller.ViewportWidth;
        if (width <= 0)
        {
            width = ActualWidth;
        }

        return width;
    }

    private double GridItemWidth() => GridSizePreset.ItemWidthFor(ViewportWidth(), _gridPreset);

    private void ApplyGridMetrics()
    {
        FileGridLayout.MinItemWidth = GridItemWidth();
        FileGridLayout.MinItemHeight = _gridPreset.ItemHeight;
        FileGridLayout.MinColumnSpacing = _gridPreset.Gutter;
        FileGridLayout.MinRowSpacing = _gridPreset.Gutter;
        UpdateAlphabetTailSpace();
    }

    private void ScheduleTileMetricsRefresh()
    {
        if (_tileMetricsPending)
        {
            return;
        }

        _tileMetricsPending = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RefreshTileMetrics))
        {
            _tileMetricsPending = false;
        }
    }

    private void RefreshTileMetrics()
    {
        _tileMetricsPending = false;
        if (!IsLoaded)
        {
            return;
        }

        try
        {
            var width = GridItemWidth();
            foreach (var tile in _tiles.ToArray())
            {
                tile.ApplyMetrics(_gridPreset, width);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(
                "Deferred tile metrics update failed: {0}",
                error);
        }
    }

    private int ViewIndexFromPoint(double x, double absoluteY) =>
        _layout == FileLayoutKind.Grid
            ? GridSizePreset.IndexFromPoint(
                x,
                absoluteY,
                _items.Count,
                Columns(),
                _gridPreset)
            : FileColumnLayout.IndexFromPoint(
                x + Scroller.HorizontalOffset,
                absoluteY,
                _items.Count,
                VisibleColumnWidth,
                0,
                0,
                0);

    private Point ToContentPoint(Point point) => new(
        point.X + Scroller.HorizontalOffset, point.Y + Scroller.VerticalOffset);

    private void StartMarqueeAutoScroll()
    {
        if (_marqueeScrollTimer is null)
        {
            _marqueeScrollTimer = DispatcherQueue.CreateTimer();
            _marqueeScrollTimer.Interval = TimeSpan.FromMilliseconds(40);
            _marqueeScrollTimer.Tick += (_, _) =>
            {
                if (!_pointerDown || !_dragging || !IsLoaded)
                {
                    _marqueeScrollTimer.Stop();
                    return;
                }
                AutoScroll(_marqueePointer.Y);
            };
        }
        _marqueeScrollTimer.Start();
    }

    private void CancelMarquee()
    {
        _marqueeScrollTimer?.Stop();
        _pointerDown = false;
        _dragging = false;
        _dragCandidate = false;
        Marquee.Visibility = Visibility.Collapsed;
        ReleasePointerCaptures();
    }

    private void UpdateMarquee()
    {
        if (!_pointerDown || !_dragging) return;
        var current = ToContentPoint(new Point(
            Math.Clamp(_marqueePointer.X, 0, Scroller.ViewportWidth),
            Math.Clamp(_marqueePointer.Y, 0, Scroller.ViewportHeight)));
        ApplyMarqueeSelection(_marqueeStart, current);
        LayoutMarquee(_marqueeStart, current);
    }

    private void AutoScroll(double y)
    {
        const double edge = 28;
        var offset = Scroller.VerticalOffset;
        if (y < edge)
        {
            _ = Scroller.ChangeView(null, Math.Max(0, offset - ItemHeight()), null, true);
        }
        else if (y > Scroller.ViewportHeight - edge)
        {
            _ = Scroller.ChangeView(null, Math.Min(Scroller.ScrollableHeight, offset + ItemHeight()), null, true);
        }
    }

    private void ApplyMarqueeSelection(Point start, Point current)
    {
        if (_items.Store is null || _items.Index is null)
        {
            return;
        }

        var left = Math.Min(start.X, current.X);
        var right = Math.Max(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var bottom = Math.Max(start.Y, current.Y);
        _marqueeHits.Clear();
        if (_layout == FileLayoutKind.Grid)
        {
            GridSizePreset.CollectIndicesInRect(
                left,
                top,
                right,
                bottom,
                _items.Count,
                Columns(),
                _gridPreset,
                _marqueeHits);
        }
        else
        {
            FileColumnLayout.CollectIndicesInRect(
                left,
                top,
                right,
                bottom,
                _items.Count,
                VisibleColumnWidth,
                0,
                0,
                0,
                _marqueeHits);
        }

        _selection.ReplaceFromViewIndices(_items.Store, _items.Index, _marqueeHits);
        RefreshRealizedSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LayoutMarquee(Point start, Point current)
    {
        // Only the visual is clipped to the viewport; selection includes offscreen items.
        var x = Math.Clamp(Math.Min(start.X, current.X) - Scroller.HorizontalOffset, 0, Scroller.ViewportWidth);
        var y = Math.Clamp(Math.Min(start.Y, current.Y) - Scroller.VerticalOffset, 0, Scroller.ViewportHeight);
        var right = Math.Clamp(Math.Max(start.X, current.X) - Scroller.HorizontalOffset, 0, Scroller.ViewportWidth);
        var bottom = Math.Clamp(Math.Max(start.Y, current.Y) - Scroller.VerticalOffset, 0, Scroller.ViewportHeight);
        Marquee.Margin = new Thickness(x, y, 0, 0);
        Marquee.Width = right - x;
        Marquee.Height = bottom - y;
    }

    private void ShowContextMenu(bool background, Point position, bool preferShell = false)
    {
        SelectionChanged?.Invoke(this, ContextSelectionChangedEventArgs.Instance);
        if (preferShell && TryShowShellContextMenu(background, position))
        {
            return;
        }

        var primaryPath = TryGetPrimaryPath();
        var selectedPaths = background ? Array.Empty<string>() : ResolveSelectionPaths();
        var archives = selectedPaths.Count > 0 && selectedPaths.All(FileTypeIconCatalog.IsArchivePath);
        var layout = FileContextMenuBuilder.BuildLayout(new CommandContext(
            CommandSurface.Menu,
            _selection.Count,
            PrimaryIsDirectory(),
            background,
            CanRefresh,
            IsFolderWritable,
            ClipboardHasFiles,
            primaryPath,
            IsPinnedPath?.Invoke(primaryPath) ?? false,
            TagsAvailable: true,
            BatchRenameAvailable: true,
            ShareAvailable: App.ShareService is not null,
            FolderPath: ExistingFolder(ResolveFolder?.Invoke()),
            PrimaryIsArchive: !PrimaryIsDirectory() && FileTypeIconCatalog.IsArchivePath(primaryPath),
            SelectionIsArchive: archives,
            OtherPanePath: OtherPanePath?.Invoke()));
        if (layout.Primary.Count == 0 && layout.Items.Count == 0)
        {
            return;
        }

        var showMore = false;
        var host = FileContextFlyout.Create(
            layout,
            id =>
            {
                if (id == AppCommandId.OpenInTerminal)
                {
                    var target = !background && _selection.Count == 1 && PrimaryIsDirectory() ? primaryPath : ResolveFolder?.Invoke();
                    if (target is not null) TerminalRequested?.Invoke(this, target);
                }
                else InvokeCommand(id);
            },
            () => showMore = true,
            column => SortRequested?.Invoke(this, column),
            SetLayout,
            CreateTagPicker?.Invoke());
        host.Closed += (_, _) =>
        {
            if (!showMore)
            {
                return;
            }

            showMore = false;
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => TryShowShellContextMenu(background, position));
        };
        FileContextFlyout.ShowAt(host, this, position);
    }

    private bool TryShowShellContextMenu(bool background, Point position)
    {
        if (App.CurrentWindow is null || XamlRoot is null)
        {
            return false;
        }

        var folder = ResolveFolder?.Invoke();
        var paths = background ? [] : ResolveSelectionPaths();
        var origin = TransformToVisual(null).TransformPoint(position);
        return ShellContextMenu.TryShow(
            App.CurrentWindow.NativeHandle,
            paths,
            folder,
            background,
            origin.X,
            origin.Y,
            XamlRoot.RasterizationScale,
            IsModifier(VirtualKey.Shift));
    }

    private void InvokeCommand(AppCommandId id)
    {
        switch (id)
        {
            case AppCommandId.SelectSameType:
                SelectSameType();
                break;
            case AppCommandId.InvertSelection:
                InvertSelection();
                break;
            case AppCommandId.Open:
                if (TryGetPrimary(out var open))
                {
                    OpenRequested?.Invoke(this, open);
                }

                break;
            case AppCommandId.OpenInNewTab:
                if (TryGetPrimary(out var tab))
                {
                    OpenInNewTabRequested?.Invoke(this, tab);
                }

                break;
            case AppCommandId.CopyPath:
                CopyPathRequested?.Invoke(this, EventArgs.Empty);
                break;
            case AppCommandId.SelectAll:
                if (_items.Store is not null && _items.Index is not null)
                {
                    _selection.SelectAll(_items.Store, _items.Index);
                    RefreshRealizedSelection();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }

                break;
            case AppCommandId.Refresh:
                RefreshRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                CommandRequested?.Invoke(this, id);
                break;
        }
    }

    public bool PrimaryIsDirectory() =>
        TryGetPrimary(out var entry) && entry.Kind == EntryKind.Directory;

    private static string? ExistingFolder(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;

    private bool TryGetPrimary(out FileEntryCore entry)
    {
        entry = default;
        if (_items.Store is null || _items.Index is null || _selection.PrimaryId is not int id)
        {
            return false;
        }

        var view = _items.Index.IndexOfId(_items.Store, id);
        return view >= 0 && _items.TryGetEntry(view, out entry);
    }

    private string? TryGetPrimaryPath()
    {
        if (!TryGetPrimary(out var entry))
        {
            return null;
        }

        return ResolvePath?.Invoke(entry);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerDown && _dragging)
        {
            // Capture belongs to this surface, so wheel input may bypass the child ScrollViewer.
            var point = e.GetCurrentPoint(Scroller);
            _marqueePointer = point.Position;
            if (!e.Handled)
                ScrollMarqueeWheel(point.Properties.MouseWheelDelta, point.Properties.IsHorizontalMouseWheel);
            e.Handled = true;
            return;
        }
        if (!IsModifier(VirtualKey.Control))
        {
            return;
        }

        e.Handled = true;
        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        _pendingZoomDirection = Math.Clamp(
            _pendingZoomDirection + (delta > 0 ? 1 : -1),
            -4,
            4);
        if (_zoomChangePending)
        {
            return;
        }

        _zoomChangePending = true;
        _pendingZoomGeneration = _generation;
        if (!DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                ApplyPendingZoom))
        {
            ResetPendingZoom();
        }
    }

    private void ScrollMarqueeWheel(int delta, bool horizontal)
    {
        if (delta == 0) return;
        const uint getWheelScrollLines = 0x0068;
        const uint getWheelScrollChars = 0x006C;
        if (!SystemParametersInfoW(horizontal ? getWheelScrollChars : getWheelScrollLines, 0, out var lines, 0))
            lines = 3;
        var viewport = horizontal ? Scroller.ViewportWidth : Scroller.ViewportHeight;
        var distance = delta / 120d * (lines == uint.MaxValue ? viewport : lines * ItemHeight());
        if (horizontal)
            Scroller.ChangeView(Math.Clamp(Scroller.HorizontalOffset + distance, 0, Scroller.ScrollableWidth), null, null, true);
        else
            Scroller.ChangeView(null, Math.Clamp(Scroller.VerticalOffset - distance, 0, Scroller.ScrollableHeight), null, true);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out uint value, uint flags);

    private void ApplyPendingZoom()
    {
        _zoomChangePending = false;
        var direction = Math.Sign(_pendingZoomDirection);
        _pendingZoomDirection = 0;
        var generation = _pendingZoomGeneration;
        _pendingZoomGeneration = -1;
        if (direction == 0 || generation != _generation || !IsLoaded)
        {
            return;
        }

        try
        {
            var next = GridSizePreset.Step(_layout, _gridPreset, direction);
            if (next.Layout == _layout && next.Preset == _gridPreset)
            {
                return;
            }

            if (next.Layout != _layout)
            {
                SetLayout(next.Layout);
                return;
            }

            if (next.Preset != _gridPreset)
            {
                SetGridSize(next.Preset);
            }
        }
        catch (Exception error)
        {
            // Zoom is an optional presentation change. A stale/unloaded
            // repeater must never be allowed to terminate the process.
            System.Diagnostics.Trace.TraceError(
                "Deferred zoom update failed: {0}",
                error);
        }
    }

    private void ResetPendingZoom()
    {
        _zoomChangePending = false;
        _pendingZoomDirection = 0;
        _pendingZoomGeneration = -1;
    }

    private static bool IsModifier(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static double Hypot(double x, double y) => Math.Sqrt((x * x) + (y * y));
}
