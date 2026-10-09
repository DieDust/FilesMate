using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Animations;
using FilesMate.App.Icons;
using FilesMate.App.Input;
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
    private bool _iconPointerOver;
    private bool _pressed;
    private bool _focused;
    private bool _dragging;
    private bool _dropTarget;
    private bool _hidden;
    private CancellationTokenSource? _sizeRequest;
    private Task? _sizeTask;
    private CancellationToken _sizeOwnerToken;
    private long _sizeVersion;
    private DetailsColumn[]? _columns;
    private TagDefinition[] _tags = [];
    private string? _entryPath;
    private double _glyphWidth = FileColumnLayout.GlyphWidth;
    internal Func<bool, ItemOpeningMode>? MemoryOpeningMode { get; set; }
    private ItemOpeningMode OpeningMode => MemoryOpeningMode?.Invoke(Entry.Kind == EntryKind.Directory) ?? App.ExplorerPreferences.OpeningMode(Entry.Kind == EntryKind.Directory);
    private readonly Dictionary<string, TextBlock> _extraCells = [];
    private readonly Dictionary<string, int> _columnSlots = [];
    private readonly FileNameHighlighting _nameHighlights;

    public FileRow()
    {
        InitializeComponent();
        RecursiveToggle.AddHandler(PointerPressedEvent,
            new PointerEventHandler(RecursiveArrow_PointerPressed), true);
        RecursiveToggle.Tapped += (_, args) => args.Handled = true;
        RecursiveToggle.DoubleTapped += (_, args) => args.Handled = true;
        RecursiveToggle.AddHandler(PointerReleasedEvent,
            new PointerEventHandler((_, args) => RecordRecursiveArrow("Released", args)), true);
        _nameHighlights = new FileNameHighlighting(NameText, NameMatchBackplates);
        ItemActivation.BindName(NameText, () => OpeningMode, cursor => ProtectedCursor = cursor);
        Unloaded += (_, _) => { CancelFolderSize(); CancelPropertyRead(); };
        ResetVisual();
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
    }

    // A presenter can remain attached while WinUI processes a collection reset.
    // Only its owner may reveal it after validating the current binding.
    internal void PresentBoundEntry()
    {
        if (EntryId < 0 || ViewIndex < 0) return;
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
    }

    public int EntryId { get; private set; } = -1;
    public event EventHandler<int>? SelectionToggleRequested;

    public int ViewIndex { get; private set; } = -1;

    internal void SetNameHighlightQuery(string query) => _nameHighlights.SetQuery(query);

    internal string? EntryPath => _entryPath;

    public FileEntryCore Entry { get; private set; }

    public void Bind(int viewIndex, in FileEntryCore entry, bool selected, string? path, CancellationToken cancellationToken = default, bool retainKnownSize = false)
    {
        // Retain work only within the same navigation cancellation scope.
        var keepWalk = _sizeOwnerToken == cancellationToken
            && string.Equals(path, _entryPath, StringComparison.OrdinalIgnoreCase)
            && Entry.Kind == EntryKind.Directory && entry.Kind == EntryKind.Directory
            && entry with { Id = Entry.Id } == Entry
            && App.ExplorerPreferences.ShowFolderSizes && !_compact
            && _sizeRequest?.IsCancellationRequested == false && _sizeTask is { IsCompleted: false };
        var previousSize = SizeText.Text;
        if (!keepWalk) CancelFolderSize();
        _sizeOwnerToken = cancellationToken;
        CancelPropertyRead();
        ViewIndex = viewIndex;
        EntryId = entry.Id;
        Entry = entry;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SelectionBox, Loc.Get("Opening_SelectItem") + ": " + entry.Name);
        _entryPath = path;
        var prefs = App.ExplorerPreferences;
        var content = FileRowFormatter.Format(entry, selected, prefs.ShowFileExtensions, prefs.DateFormat);
        NameText.Text = content.Name;
        _nameHighlights.Refresh();
        ToolTipService.SetToolTip(NameText, entry.Name);
        ModifiedText.Text = content.Modified;
        BindFileType(entry, content.Type);
        SizeText.Text = content.Size;
        UpdateExtraCells();
        Glyph.Glyph = FileRowFormatter.Glyph(entry);
        ShellIconBinder.Bind(IconImage, Glyph, entry, _recursiveIdentityVerified ? path : null,
            (int)FileColumnLayout.DetailsIconSize, _thumbnailLoadMode);
        _hidden = content.IsHidden;
        _selected = content.IsSelected;
        UpdateState(animate: false);
        if (keepWalk) SizeText.Text = previousSize;
        else if (!_recursiveRow) RequestFolderSize(path, cancellationToken, retainKnownSize);
        RefreshProperties();
    }

    private void BindFileType(in FileEntryCore entry, string description)
    {
        var appearance = FileTypeAppearance.For(entry.Name, entry.Kind == EntryKind.Directory);
        var badge = !string.IsNullOrEmpty(appearance.Extension);
        TypeText.Text = badge ? appearance.Extension : description;
        // All types share the same badge geometry. Keep each badge's paired
        // foreground/background instead of replacing its ink on selection.
        TypeCell.Style = (Style)Application.Current.Resources[$"FilesMate.Type.{appearance.Tone}Style"];
        TypeText.Style = (Style)Application.Current.Resources[$"FilesMate.Type.{appearance.Tone}TextStyle"];
        TypeText.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTipService.SetToolTip(TypeCell, description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TypeCell, description);
    }

    private void RequestFolderSize(string? path, CancellationToken cancellationToken, bool retainKnownSize)
    {
        if (cancellationToken.IsCancellationRequested || _recursiveRow
            || _compact || Entry.Kind != EntryKind.Directory
            || !App.ExplorerPreferences.ShowFolderSizes
              || string.IsNullOrEmpty(path)
              || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out _)
            || (Entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
        {
            return;
        }

        if (FolderSizeCache.TryGet(path, out var cached)
            || (retainKnownSize && FolderSizeCache.TryGetUnchanged(path, out cached)))
        {
            SizeText.Text = DriveCapacity.FormatBytes(ToDisplayBytes(cached));
            return;
        }

        // A still-visible request keeps its existing walk across a fast sample.
        if (_sizeRequest?.IsCancellationRequested == false && _sizeTask is { IsCompleted: false }) return;

        var hadKnown = FolderSizeCache.TryGetLastKnown(path, out var previous);
        SizeText.Text = hadKnown ? DriveCapacity.FormatBytes(ToDisplayBytes(previous)) : "…";
        // Cached values remain cheap text during fast movement. Cold walks
        // start only when the current row becomes active after viewport settle.
        if (_thumbnailLoadMode != ThumbnailLoadMode.Active) return;
        _sizeRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sizeOwnerToken = cancellationToken;
        _sizeTask = FillFolderSizeAsync(_sizeVersion, path, _sizeRequest.Token, hadKnown);
    }

    private async Task FillFolderSizeAsync(long version, string path, CancellationToken cancellationToken, bool hadKnown)
    {
        try
        {
            var bytes = await FolderSizeCache.GetAsync(path, cancellationToken, hadKnown ? null : Report).ConfigureAwait(false);
            Report(bytes);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (_sizeVersion != version || !IsLoaded || _sizeOwnerToken.IsCancellationRequested) return;
                    var owner = _sizeOwnerToken;
                    CancelFolderSize();
                    RequestFolderSize(path, owner, retainKnownSize: false);
                });
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

    internal void CancelFolderSizeWithin(IReadOnlyList<string> paths)
    {
        if (_entryPath is { } path && paths.Any(root => FilesMate.Platform.Windows.Locks.FileLockPath.Matches(path, root, directory: true)))
            CancelFolderSize();
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
        var badgeWidth = Math.Min(68, Math.Max(0, (columns.FirstOrDefault(c => c.Id == DetailsColumnId.Type)?.Width ?? 84) - 16));
        if (TypeCell.Width != badgeWidth) TypeCell.Width = badgeWidth;
        var sameLayout = _columns?.Length == columns.Length;
        if (sameLayout)
            for (var i = 0; i < columns.Length; i++)
                if (_columns![i].Key != columns[i].Key || _columns[i].Visible != columns[i].Visible) { sameLayout = false; break; }
        var tagsWidthChanged = _columns?.FirstOrDefault(c => c.Id == DetailsColumnId.Tags)?.Width
            != columns.FirstOrDefault(c => c.Id == DetailsColumnId.Tags)?.Width;
        _columns = columns;
        if (sameLayout)
        {
            // Width drags update only the changed definition, without rebuilding every realized row.
            foreach (var column in columns)
                if (_columnSlots.TryGetValue(column.Key, out var slot))
                {
                    var width = column.Visible ? column.Width : 0;
                    if (Root.ColumnDefinitions[slot].Width.Value != width) Root.ColumnDefinitions[slot].Width = new GridLength(width);
                }
            Width = FileColumnLayout.RowWidth(columns.Where(c => c.Visible).Sum(c => c.Width) + _glyphWidth - FileColumnLayout.GlyphWidth, 0, 0, 0);
            if (tagsWidthChanged) RenderTags();
            return;
        }
        CancelPropertyRead();
        _columnSlots.Clear();
        foreach (var cell in _extraCells.Values) cell.Visibility = Visibility.Collapsed;
        Root.ColumnDefinitions.Clear();
        Root.ColumnDefinitions.Add(new() { Width = new GridLength(FileColumnLayout.AccentWidth) });
        foreach (var column in columns)
        {
            var position = Root.ColumnDefinitions.Count;
            if (column.Id == DetailsColumnId.Name)
            {
                Root.ColumnDefinitions.Add(new() { Width = new GridLength(_glyphWidth) });
                Grid.SetColumn(NamePresenter, position++);
                Grid.SetColumnSpan(NamePresenter, 2);
            }
            else
            {
                FrameworkElement cell;
                switch (column.Id)
                {
                    case DetailsColumnId.Modified: cell = ModifiedText; break;
                    case DetailsColumnId.Type: cell = TypeCell; break;
                    case DetailsColumnId.Size: cell = SizeText; break;
                    case DetailsColumnId.Tags: cell = TagCell; break;
                    default:
                        if (!_extraCells.TryGetValue(column.Key, out var extra))
                        {
                            if (!column.Visible) continue;
                            extra = new TextBlock { Padding = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["FilesMate.DetailsMetadataTextStyle"] };
                            _extraCells.Add(column.Key, extra);
                            _appliedTypography = null;
                            Root.Children.Add(extra);
                        }
                        cell = extra;
                        break;
                }
                Grid.SetColumn(cell, position);
                cell.Visibility = column.Visible ? Visibility.Visible : Visibility.Collapsed;
            }
            Root.ColumnDefinitions.Add(new() { Width = new GridLength(column.Visible ? column.Width : 0) });
            _columnSlots[column.Key] = position;
        }
        Root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumnSpan(Fill, Root.ColumnDefinitions.Count);
        Grid.SetColumnSpan(Stripe, Root.ColumnDefinitions.Count);
        Grid.SetColumnSpan(FocusVisual, Root.ColumnDefinitions.Count);
        Width = FileColumnLayout.RowWidth(columns.Where(c => c.Visible).Sum(c => c.Width) + _glyphWidth - FileColumnLayout.GlyphWidth, 0, 0, 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        UpdateExtraCells();
        RenderTags();
        RefreshProperties();
        ApplyTypography(App.AppearanceViewModel?.Current ?? AppearanceSettings.Default);
        UpdateMetadataInk();
    }

    private (string? Font, double Name, double Details, double Height)? _appliedTypography;

    internal void ApplyTypography(AppearanceSettings settings, double? scale = null)
    {
        var height = settings.FileRowHeightForScale(scale ?? XamlRoot?.RasterizationScale ?? 1);
        var next = (settings.FileFontFamily, settings.FileNameFontSize, settings.FileDetailsFontSize, height);
        if (_appliedTypography is { } previous && previous == next) return;
        _appliedTypography = next;
        Height = height;
        UseLayoutRounding = true;
        FileTypography.Apply(NameText, settings, settings.FileNameFontSize);
        TypeCell.Height = Math.Max(20, Math.Ceiling(settings.FileDetailsFontSize * 1.4) + 2);
        foreach (var text in _extraCells.Values.Append(ModifiedText).Append(SizeText))
            FileTypography.Apply(text, settings, settings.FileDetailsFontSize);
        FileTypography.Apply(TypeText, settings, settings.FileDetailsFontSize - 1);
    }

    private void UpdateExtraCells()
    {
        foreach (var (key, cell) in _extraCells)
        {
            if (cell.Visibility != Visibility.Visible) continue;
            var id = _columns?.FirstOrDefault(c => c.Key == key)?.Id;
            cell.Text = id switch
            {
                DetailsColumnId.Created => FileRowFormatter.FormatModified(Entry.CreatedUtcTicks, App.ExplorerPreferences.DateFormat),
                DetailsColumnId.Accessed => FileRowFormatter.FormatModified(Entry.AccessedUtcTicks, App.ExplorerPreferences.DateFormat),
                DetailsColumnId.Location => _recursiveRow ? _recursiveRelativeParent : _entryPath is null ? "" : Path.GetDirectoryName(_entryPath) ?? "",
                DetailsColumnId.FullPath => _entryPath ?? "",
                DetailsColumnId.Extension => Entry.Kind == EntryKind.Directory ? "" : Path.GetExtension(Entry.Name),
                DetailsColumnId.ShellProperty => "",
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

    public void SetTags(IEnumerable<TagDefinition>? tags)
    {
        _tags = tags?.ToArray() ?? [];
        RenderTags();
    }

    private void RenderTags() => TagVisuals.Apply(TagHost, _tags,
        availableWidth: Math.Max(0, (_columns?.FirstOrDefault(c => c.Id == DetailsColumnId.Tags)?.Width ?? 176) - 16));

    private bool _compact;
    internal void SetIconScale(double scale)
    {
        var size = FileColumnLayout.DetailsIconSize * scale;
        var heightChanged = IconFrame.Height != size;
        if (IconFrame.Width != size) IconFrame.Width = size;
        if (heightChanged) IconFrame.Height = size;
        if (IconImage.Width != size) IconImage.Width = size;
        if (IconImage.Height != size) IconImage.Height = size;
        if (Glyph.FontSize != 16 * scale) Glyph.FontSize = 16 * scale;
        var width = Math.Max(FileColumnLayout.GlyphWidth, size + 4);
        if (_glyphWidth == width)
        {
            if (heightChanged) UpdateRecursiveLeadingLayout();
            return;
        }
        _glyphWidth = width;
        UpdateRecursiveLeadingLayout();
        if (_columns is { } columns) { _columns = null; ApplyColumns(columns); }
    }
    public void SetCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        UpdateState(animate: false);
    }

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
        NameText.TextDecorations = Windows.UI.Text.TextDecorations.None;
        ProtectedCursor = null;
        _selected = false;
        _pointerOver = false;
        _iconPointerOver = false;
        _pressed = false;
        _focused = false;
        _dragging = false;
        _dropTarget = false;
        _hidden = false;
        Opacity = 1;
        RenderTransform = null;
        UpdateState(animate: false);
    }

    public void Clear() => ClearCore(recycling: false);

    // The factory detaches after the repeater finishes its child traversal.
    // Bind overwrites every content cell before it can become visible again.
    // Avoid shaping empty text and removing/reapplying the same native style
    // on each scroll step; retirement still uses the full Clear path.
    internal void ClearForRecycle() => ClearCore(recycling: true);

    private void ClearCore(bool recycling)
    {
        // Content cleanup is not a visual retirement: suppress the whole old
        // hit area before reset/recycling can expose its empty default glyph.
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        SetRecursivePresentation(null, false, false, null);
        CancelFolderSize();
        CancelPropertyRead();
        ViewIndex = -1;
        EntryId = -1;
        Entry = default;
        _entryPath = null;
        if (!recycling) foreach (var cell in _extraCells.Values)
        {
            cell.Text = string.Empty;
            ToolTipService.SetToolTip(cell, null);
        }
        if (!recycling) NameText.Text = string.Empty;
        _nameHighlights.SetQuery("");
        if (!recycling)
        {
            ToolTipService.SetToolTip(NameText, null);
            ModifiedText.Text = string.Empty;
            TypeText.Text = string.Empty;
            TypeCell.Style = null;
            ToolTipService.SetToolTip(TypeCell, null);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TypeCell, "");
            SizeText.Text = string.Empty;
        }
        _tags = [];
        RenderTags();
        if (!recycling) Glyph.Glyph = "\uE8A5";
        ShellIconBinder.Clear(IconImage, Glyph);
        ResetVisual();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = OpeningMode == ItemOpeningMode.SingleClick ? ItemActivation.HandCursor : null;
        _pointerOver = true;
        UpdateState(animate: false);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        _iconPointerOver = false;
        _pressed = false;
        UpdateState(animate: false);
    }

    private void OnIconPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _iconPointerOver = true;
        ProtectedCursor = null;
        UpdateSelectionBox();
    }

    private void OnIconPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _iconPointerOver = false;
        UpdateSelectionBox();
    }

    private void OnSelectionBoxClick(object sender, RoutedEventArgs e)
    {
        _pressed = false;
        if (EntryId >= 0) SelectionToggleRequested?.Invoke(this, EntryId);
        UpdateState(animate: false);
    }

    private void UpdateSelectionBox()
    {
        var show = EntryId >= 0 && _iconPointerOver && !_dragging && !_dropTarget;
        SelectionBox.IsChecked = _selected;
        SelectionBox.Visibility = SelectionOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // Keep the graphic measured, so replacing it with the checkbox cannot move the name.
        IconImage.Opacity = Glyph.Opacity = show ? 0 : 1;
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
        UpdateSelectionBox();
        var state = FileRowVisualStates.Resolve(
            _selected,
            _pointerOver,
            _pressed,
            _focused,
            _dragging,
            _dropTarget);
        var useTransitions = animate && App.Motion.Resolve(MotionDurations.Hover) > TimeSpan.Zero;
        VisualStateManager.GoToState(this, state, useTransitions);
        VisualStateManager.GoToState(this,
            FileRowVisualStates.ResolveFocus(_focused, _selected, _pressed, _dragging, _dropTarget), false);
        UpdateMetadataInk();
        Stripe.Visibility = App.ExplorerPreferences.ShowAlternatingRows && !_compact && ViewIndex >= 0 && (ViewIndex & 1) != 0
            && state is FileRowVisualStates.Normal or FileRowVisualStates.Focused
            ? Visibility.Visible : Visibility.Collapsed;
        // Hidden is a file-content cue. Selection, focus and text keep their full ink.
        Opacity = 1;
        var iconOpacity = SelectionBox.Visibility == Visibility.Visible ? 0
            : _hidden && !Theming.GlassEnvironment.HighContrast ? HiddenOpacity() : 1;
        Glyph.Opacity = IconImage.Opacity = iconOpacity;
    }

    private static double HiddenOpacity()
    {
        return Application.Current.Resources.TryGetValue("FilesMate.Item.HiddenOpacity", out var value)
            && value is double opacity
            ? opacity
            : 0.48;
    }

    private void UpdateMetadataInk()
    {
        var active = _dropTarget || !_dragging && (_selected || _pressed);
        foreach (var cell in _extraCells.Values) ApplyInk(cell);
        // Every type badge retains its own paired foreground and background.
        Theming.ThemeResources.Clear(TypeText, TextBlock.ForegroundProperty);

        void ApplyInk(TextBlock cell)
        {
            if (active) Theming.ThemeResources.Bind(cell, TextBlock.ForegroundProperty,
                "FilesMate.FileItem.SelectedForegroundBrush");
            else if (_pointerOver) Theming.ThemeResources.Bind(cell, TextBlock.ForegroundProperty,
                "FilesMate.FileItem.HoverForegroundBrush");
            else Theming.ThemeResources.Clear(cell, TextBlock.ForegroundProperty);
        }
    }
}
