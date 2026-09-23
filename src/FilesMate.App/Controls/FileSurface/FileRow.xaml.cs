using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Animations;
using FilesMate.App.Icons;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Entries;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileRow : UserControl
{
    private bool _selected;
    private bool _pointerOver;
    private bool _pressed;
    private bool _focused;
    private bool _dragging;
    private bool _dropTarget;
    private bool _hidden;
    private CancellationTokenSource? _sizeRequest;
    private long _sizeVersion;
    private DetailsColumn[]? _columns;
    private string? _entryPath;
    private readonly Dictionary<DetailsColumnId, TextBlock> _extraCells = [];
    private readonly Dictionary<DetailsColumnId, int> _columnSlots = [];

    public FileRow()
    {
        InitializeComponent();
        Unloaded += (_, _) => CancelFolderSize();
        ResetVisual();
    }

    public int EntryId { get; private set; } = -1;

    public int ViewIndex { get; private set; } = -1;

    public FileEntryCore Entry { get; private set; }

    public void Bind(int viewIndex, in FileEntryCore entry, bool selected, string? path, CancellationToken cancellationToken = default)
    {
        CancelFolderSize();
        ViewIndex = viewIndex;
        EntryId = entry.Id;
        Entry = entry;
        _entryPath = path;
        var prefs = App.ExplorerPreferences;
        var content = FileRowFormatter.Format(entry, selected, prefs.ShowFileExtensions, prefs.DateFormat);
        NameText.Text = content.Name;
        ToolTipService.SetToolTip(NameText, entry.Name);
        ModifiedText.Text = content.Modified;
        TypeText.Text = content.Type;
        SizeText.Text = content.Size;
        UpdateExtraCells();
        Glyph.Glyph = FileRowFormatter.Glyph(entry);
        ShellIconBinder.Bind(IconImage, Glyph, entry, path, (int)FileColumnLayout.DetailsIconSize);
        _hidden = content.IsHidden;
        _selected = content.IsSelected;
        UpdateState(animate: false);
        RequestFolderSize(path, cancellationToken);
    }

    private void RequestFolderSize(string? path, CancellationToken cancellationToken)
    {
        if (Entry.Kind != EntryKind.Directory
            || !App.ExplorerPreferences.ShowFolderSizes
              || string.IsNullOrEmpty(path)
              || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out _)
            || (Entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
        {
            return;
        }

        if (FolderSizeCache.TryGet(path, out var cached))
        {
            SizeText.Text = DriveCapacity.FormatBytes(ToDisplayBytes(cached));
            return;
        }

        SizeText.Text = "…";
        _sizeRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = FillFolderSizeAsync(_sizeVersion, path, _sizeRequest.Token);
    }

    private async Task FillFolderSizeAsync(long version, string path, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await FolderSizeCache.GetAsync(path, cancellationToken, Report).ConfigureAwait(false);
            Report(bytes);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }

        void Report(ulong value)
        {
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (!cancellationToken.IsCancellationRequested && _sizeVersion == version)
                {
                    SizeText.Text = DriveCapacity.FormatBytes(ToDisplayBytes(value));
                }
            });
        }
    }

    private void CancelFolderSize()
    {
        ++_sizeVersion;
        _sizeRequest?.Cancel();
        _sizeRequest?.Dispose();
        _sizeRequest = null;
    }

    private static long ToDisplayBytes(ulong bytes) =>
        bytes > long.MaxValue ? long.MaxValue : (long)bytes;

    public void ApplyColumns(double name, double modified, double type, double size)
    {
        NameColumn.Width = new GridLength(name);
        ModifiedColumn.Width = new GridLength(modified);
        TypeColumn.Width = new GridLength(type);
        SizeColumn.Width = new GridLength(size);
        Width = FileColumnLayout.RowWidth(name, modified, type, size);
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public void ApplyColumns(DetailsColumn[] columns)
    {
        if (ReferenceEquals(_columns, columns)) return;
        var sameLayout = _columns?.Length == columns.Length;
        if (sameLayout)
            for (var i = 0; i < columns.Length; i++)
                if (_columns![i].Id != columns[i].Id || _columns[i].Visible != columns[i].Visible) { sameLayout = false; break; }
        _columns = columns;
        if (sameLayout)
        {
            // Width drags update only the changed definition, without rebuilding every realized row.
            foreach (var column in columns)
                if (_columnSlots.TryGetValue(column.Id, out var slot))
                {
                    var width = column.Visible ? column.Width : 0;
                    if (Root.ColumnDefinitions[slot].Width.Value != width) Root.ColumnDefinitions[slot].Width = new GridLength(width);
                }
            Width = FileColumnLayout.RowWidth(columns.Where(c => c.Visible).Sum(c => c.Width), 0, 0, 0);
            return;
        }
        _columnSlots.Clear();
        Root.ColumnDefinitions.Clear();
        Root.ColumnDefinitions.Add(new() { Width = new GridLength(FileColumnLayout.AccentWidth) });
        foreach (var column in columns)
        {
            var position = Root.ColumnDefinitions.Count;
            if (column.Id == DetailsColumnId.Name)
            {
                Root.ColumnDefinitions.Add(new() { Width = new GridLength(FileColumnLayout.GlyphWidth) });
                Grid.SetColumn(IconFrame, position++);
                Grid.SetColumn(NameCell, position);
            }
            else
            {
                TextBlock cell;
                switch (column.Id)
                {
                    case DetailsColumnId.Modified: cell = ModifiedText; break;
                    case DetailsColumnId.Type: cell = TypeText; break;
                    case DetailsColumnId.Size: cell = SizeText; break;
                    default:
                        if (!_extraCells.TryGetValue(column.Id, out cell!))
                        {
                            if (!column.Visible) continue;
                            cell = new TextBlock { Padding = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis, Style = ModifiedText.Style, Foreground = ModifiedText.Foreground };
                            _extraCells.Add(column.Id, cell);
                            Root.Children.Add(cell);
                        }
                        break;
                }
                Grid.SetColumn(cell, position);
                cell.Visibility = column.Visible ? Visibility.Visible : Visibility.Collapsed;
            }
            Root.ColumnDefinitions.Add(new() { Width = new GridLength(column.Visible ? column.Width : 0) });
            _columnSlots[column.Id] = position;
        }
        Grid.SetColumnSpan(Fill, Root.ColumnDefinitions.Count);
        Width = FileColumnLayout.RowWidth(columns.Where(c => c.Visible).Sum(c => c.Width), 0, 0, 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        UpdateExtraCells();
    }

    private void UpdateExtraCells()
    {
        foreach (var (id, cell) in _extraCells)
        {
            if (cell.Visibility != Visibility.Visible) continue;
            cell.Text = id switch
            {
                DetailsColumnId.Created => FileRowFormatter.FormatModified(Entry.CreatedUtcTicks, App.ExplorerPreferences.DateFormat),
                DetailsColumnId.Accessed => FileRowFormatter.FormatModified(Entry.AccessedUtcTicks, App.ExplorerPreferences.DateFormat),
                DetailsColumnId.Location => _entryPath is null ? "" : Path.GetDirectoryName(_entryPath) ?? "",
                DetailsColumnId.FullPath => _entryPath ?? "",
                DetailsColumnId.Extension => Entry.Kind == EntryKind.Directory ? "" : Path.GetExtension(Entry.Name),
                _ => FormatAttributes(Entry.Attributes)
            };
            ToolTipService.SetToolTip(cell, cell.Text);
        }
    }

    private static string FormatAttributes(FileAttributes value)
    {
        var labels = new List<string>(4);
        if (value.HasFlag(FileAttributes.ReadOnly)) labels.Add(Loc.Get("Preview_ReadOnly"));
        if (value.HasFlag(FileAttributes.Hidden)) labels.Add(Loc.Get("Preview_Hidden"));
        if (value.HasFlag(FileAttributes.System)) labels.Add(Loc.Get("Preview_System"));
        if (value.HasFlag(FileAttributes.Archive)) labels.Add(Loc.Get("Attribute_Archive"));
        if (value.HasFlag(FileAttributes.Compressed)) labels.Add(Loc.Get("Attribute_Compressed"));
        if (value.HasFlag(FileAttributes.Encrypted)) labels.Add(Loc.Get("Attribute_Encrypted"));
        if (value.HasFlag(FileAttributes.ReparsePoint)) labels.Add(Loc.Get("Attribute_Link"));
        if (value.HasFlag(FileAttributes.Offline)) labels.Add(Loc.Get("Attribute_Offline"));
        return string.Join("、", labels);
    }

    public void SetTags(IEnumerable<TagDefinition>? tags) => TagVisuals.Apply(TagHost, tags);

    public void SetSelected(bool selected)
    {
        _selected = selected;
        UpdateState();
    }

    public void SetFocused(bool focused)
    {
        _focused = focused;
        UpdateState();
    }

    public void SetDragging(bool dragging)
    {
        _dragging = dragging;
        _pressed = false;
        UpdateState();
    }

    public void SetDropTarget(bool dropTarget)
    {
        if (_dropTarget == dropTarget)
        {
            return;
        }

        _dropTarget = dropTarget;
        UpdateState(animate: false);
    }

    public void ResetVisual()
    {
        _selected = false;
        _pointerOver = false;
        _pressed = false;
        _focused = false;
        _dragging = false;
        _dropTarget = false;
        _hidden = false;
        Opacity = 1;
        RenderTransform = null;
        UpdateState(animate: false);
    }

    public void Clear()
    {
        CancelFolderSize();
        ViewIndex = -1;
        EntryId = -1;
        Entry = default;
        _entryPath = null;
        foreach (var cell in _extraCells.Values)
        {
            cell.Text = string.Empty;
            ToolTipService.SetToolTip(cell, null);
        }
        NameText.Text = string.Empty;
        ToolTipService.SetToolTip(NameText, null);
        ModifiedText.Text = string.Empty;
        TypeText.Text = string.Empty;
        SizeText.Text = string.Empty;
        TagHost.Children.Clear();
        Glyph.Glyph = "\uE8A5";
        ShellIconBinder.Clear(IconImage, Glyph);
        ResetVisual();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = true;
        UpdateState(animate: false);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        _pressed = false;
        UpdateState(animate: false);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressed = true;
        UpdateState(animate: false);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        UpdateState(animate: false);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        UpdateState(animate: false);
    }

    private void UpdateState(bool animate = true)
    {
        var state = FileRowVisualStates.Resolve(
            _selected,
            _pointerOver,
            _pressed,
            _focused,
            _dragging,
            _dropTarget);
        var useTransitions = animate && App.Motion.Resolve(MotionDurations.Hover) > TimeSpan.Zero;
        VisualStateManager.GoToState(this, state, useTransitions);
        if (state != FileRowVisualStates.Dragging)
        {
            Opacity = _hidden ? HiddenOpacity() : 1;
        }
    }

    private static double HiddenOpacity()
    {
        return Application.Current.Resources.TryGetValue("FilesMate.Item.HiddenOpacity", out var value)
            && value is double opacity
            ? opacity
            : 0.48;
    }
}
