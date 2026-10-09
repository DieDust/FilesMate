using FilesMate.App.Animations;
using FilesMate.App.Icons;
using FilesMate.App.Input;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Models;
using FilesMate.Core.Entries;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileTile : UserControl
{
    private bool _selected;
    private bool _pointerOver;
    private bool _pressed;
    private bool _focused;
    private bool _dragging;
    private bool _dropTarget;
    private bool _hidden;
    private GridSizePreset _preset = GridSizePreset.Default;
    private string? _previewPath;
    private int _previewPixels;
    private string? _path;
    private int _iconDipSize;
    private double _iconRasterScale;
    private ThumbnailQuality _iconQuality;
    private bool _iconShowFullThumbnails;
    private int _iconCacheGeneration;
    private TagDefinition[] _tags = [];
    private readonly FileNameHighlighting _nameHighlights;
    internal Func<bool, ItemOpeningMode>? MemoryOpeningMode { get; set; }
    private ItemOpeningMode OpeningMode => MemoryOpeningMode?.Invoke(Entry.Kind == EntryKind.Directory) ?? App.ExplorerPreferences.OpeningMode(Entry.Kind == EntryKind.Directory);

    public FileTile()
    {
        InitializeComponent();
        _nameHighlights = new FileNameHighlighting(NameText, NameMatchBackplates);
        ItemActivation.BindName(NameText, () => OpeningMode, cursor => ProtectedCursor = cursor);
        Unloaded += (_, _) => { FolderPreviewBinder.Clear(FolderPreviewHost, FolderPreviewCover); CancelTileFolderSize(); };
        Loaded += (_, _) => { BindPreview(); UpdateSizeText(); };
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

    internal string? EntryPath => _path;

    public FileEntryCore Entry { get; private set; }

    public void ApplyMetrics(GridSizePreset preset, double itemWidth, bool rebindImages = true)
    {
        _preset = preset;
        if (Width != itemWidth)
        {
            Width = itemWidth;
        }

        if (Height != preset.ItemHeight)
        {
            Height = preset.ItemHeight;
        }

        // Selection chrome belongs to the grid cell, not the measured filename or tags.
        var chromeWidth = Math.Max(preset.IconSize, itemWidth - 2 * GridSizePreset.HighlightPad);
        if (Root.Width != chromeWidth) Root.Width = chromeWidth;
        // Each row and each border/padding edge rounds separately at high DPI.
        // Reserve their full physical height so the bottom border stays visible.
        Root.Height = preset.ChromeHeightForScale(XamlRoot?.RasterizationScale ?? 1);

        if (IconHost.Width != preset.IconSize)
        {
            IconHost.Width = preset.IconSize;
            IconHost.Height = preset.IconSize;
            Glyph.FontSize = Math.Max(16, preset.IconSize * 0.5);
        }

        var nameWidth = Math.Max(preset.IconSize, itemWidth - (2 * GridSizePreset.HighlightPad));
        var width = double.NaN;
        if (!NameText.Width.Equals(width) || NameText.MaxWidth != nameWidth)
        {
            NameText.Width = width;
            NameText.MaxWidth = nameWidth;
            TagHost.MaxWidth = nameWidth;
        }

        if (NameText.MaxHeight != preset.TextHeight)
        {
            NameText.MaxHeight = preset.TextHeight;
        }

        // A shared two-line name area keeps every highlight the same size.
        // Tags sit below it instead of stretching the selection background.
        NameText.Height = preset.TextHeight;
        NameText.LineHeight = preset.TextHeight / 2;
        NameText.Margin = new Thickness(0, GridSizePreset.TileIconTextGap, 0, 0);
        SizeText.MaxWidth = nameWidth;
        SizeText.Height = preset.SizeHeight;
        SizeText.Margin = new Thickness(0, preset.ShowsFileSize ? GridSizePreset.TileSizeGap : 0, 0, 0);
        UpdateSizeText();
        TagHost.Margin = new Thickness(0, GridSizePreset.TileTagGap, 0, 0);
        TagHost.Height = preset.TagHeight;
        RenderTags();

        var scale = XamlRoot?.RasterizationScale ?? 1;
        var dipSize = (int)Math.Round(preset.IconSize);
        if (rebindImages && ViewIndex >= 0 && (_iconDipSize != dipSize || _iconRasterScale != scale)) BindIcon();
        var pixels = ThumbnailQualityPolicy.PixelSize(
            Math.Clamp((int)Math.Ceiling(preset.IconSize * scale), 32, 256), App.ExplorerPreferences.ThumbnailQuality);
        if (_previewPixels != pixels)
        {
            _previewPixels = pixels;
            if (rebindImages && _previewPath is not null)
                BindPreview();
        }
    }

    public void Bind(int viewIndex, in FileEntryCore entry, bool selected, string? path, CancellationToken cancellationToken = default, bool retainKnownSize = false)
    {
        var sameEntry = EntryId >= 0 && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase) && Entry == entry;
        var prefs = App.ExplorerPreferences;
        var keepIcon = sameEntry && IconImage.Source is not null && _iconQuality == prefs.ThumbnailQuality
            && _iconShowFullThumbnails == prefs.ShowFullThumbnails && _iconCacheGeneration == ShellIconBinder.CacheGeneration
            && _iconDipSize == (int)Math.Round(_preset.IconSize) && _iconRasterScale == (XamlRoot?.RasterizationScale ?? 1);
        // A new navigation scope must own its own shared-service subscriber.
        if (!sameEntry || _tileSizeOwner != cancellationToken) CancelTileFolderSize();
        _tileSizeOwner = cancellationToken;
        _tileRetainKnownSize = retainKnownSize;
        ApplyTypography(App.AppearanceViewModel?.Current ?? AppearanceSettings.Default);
        ViewIndex = viewIndex;
        EntryId = entry.Id;
        Entry = entry;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SelectionBox, Localization.StringTable.Get("Opening_SelectItem") + ": " + entry.Name);
        NameText.Text = FileRowFormatter.DisplayName(entry, prefs.ShowFileExtensions);
        _nameHighlights.Refresh();
        ToolTipService.SetToolTip(Root, entry.Name);
        Glyph.Glyph = FileRowFormatter.Glyph(entry);
        _path = path;
        IconImage.Stretch = !prefs.ShowFullThumbnails && (FileTypeIconCatalog.Classify(entry) is FileIconKind.Image or FileIconKind.Video)
            ? Stretch.UniformToFill : Stretch.Uniform;
        FolderPreviewCover.Stretch = prefs.ShowFullThumbnails ? Stretch.Uniform : Stretch.UniformToFill;
        // Size/status updates must not decode another copy of a retained Ultra thumbnail.
        if (!keepIcon) BindIcon();
        UpdateSizeText();
        var isFolder = entry.Kind == EntryKind.Directory;
        _previewPath = isFolder ? path : null;
        if (!sameEntry) BindPreview();
        _hidden = FileRowFormatter.IsGhosted(entry.Attributes);
        _selected = selected;
        UpdateState(animate: false);
    }

    internal void ApplyTypography(AppearanceSettings settings)
    {
        FileTypography.Apply(NameText, settings, settings.FileNameFontSize);
        FileTypography.Apply(SizeText, settings, settings.FileDetailsFontSize);
        NameText.TextLineBounds = TextLineBounds.Full;
        NameText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
    }

    private void BindIcon()
    {
        _iconDipSize = (int)Math.Round(_preset.IconSize);
        _iconRasterScale = XamlRoot?.RasterizationScale ?? 1;
        _iconQuality = App.ExplorerPreferences.ThumbnailQuality;
        _iconShowFullThumbnails = App.ExplorerPreferences.ShowFullThumbnails;
        _iconCacheGeneration = ShellIconBinder.CacheGeneration;
        ShellIconBinder.Bind(IconImage, Glyph, Entry, _path, _iconDipSize, _thumbnailLoadMode);
    }

    private void UpdateSizeText()
    {
        var show = ViewIndex >= 0 && _preset.ShowsFileSize
            && (Entry.Kind == EntryKind.File || App.ExplorerPreferences.ShowFolderSizes && CanMeasureTileFolder());
        if (show && Entry.Kind == EntryKind.Directory) UpdateTileFolderSize();
        else
        {
            CancelTileFolderSize();
            SizeText.Text = show ? FileRowFormatter.FormatSize(Entry) : string.Empty;
        }
        SizeText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetTags(IEnumerable<TagDefinition>? tags)
    {
        _tags = tags?.ToArray() ?? [];
        RenderTags();
    }

    private void RenderTags() => TagVisuals.Apply(TagHost, _tags, showNames: _preset.ShowsTagNames,
        availableWidth: double.IsFinite(TagHost.MaxWidth) ? TagHost.MaxWidth : _preset.ChromeWidth, maxVisible: 1);

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

    internal void ClearForRecycle() => ClearCore(recycling: true);

    private void ClearCore(bool recycling)
    {
        // Content cleanup is not a visual retirement: suppress the whole old
        // hit area before reset/recycling can expose its empty default glyph.
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        CancelTileFolderSize();
        ViewIndex = -1;
        EntryId = -1;
        Entry = default;
        if (!recycling) NameText.Text = string.Empty;
        _nameHighlights.SetQuery("");
        if (!recycling)
        {
            SizeText.Text = string.Empty;
            SizeText.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(Root, null);
            Glyph.Glyph = "\uE8B7";
        }
        _tags = [];
        RenderTags();
        if (!recycling) IconImage.Stretch = Stretch.Uniform;
        ShellIconBinder.Clear(IconImage, Glyph);
        _previewPath = null;
        _path = null;
        FolderPreviewBinder.Clear(FolderPreviewHost, FolderPreviewCover);
        ResetVisual();
    }

    private void BindPreview() =>
        FolderPreviewBinder.Bind(FolderPreviewHost, FolderPreviewCover, _previewPath, _previewPixels, _thumbnailLoadMode);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = OpeningMode == ItemOpeningMode.SingleClick ? ItemActivation.HandCursor : null;
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
        SelectionBox.IsChecked = _selected;
        SelectionBox.Visibility = SelectionOverlay.Visibility = EntryId >= 0 && _pointerOver && !_dragging && !_dropTarget
            ? Visibility.Visible : Visibility.Collapsed;
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
        // Preserve the chrome and text even for hidden selected/focused files.
        Opacity = 1;
        IconHost.Opacity = _hidden && !Theming.GlassEnvironment.HighContrast ? HiddenOpacity() : 1;
    }

    private void OnSelectionBoxClick(object sender, RoutedEventArgs e)
    {
        _pressed = false;
        if (EntryId >= 0) SelectionToggleRequested?.Invoke(this, EntryId);
        UpdateState(animate: false);
    }


    private static double HiddenOpacity()
    {
        return Application.Current.Resources.TryGetValue("FilesMate.Item.HiddenOpacity", out var value)
            && value is double opacity
            ? opacity
            : 0.48;
    }
}
