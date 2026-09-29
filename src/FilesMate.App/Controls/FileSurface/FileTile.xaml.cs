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
    private TagDefinition[] _tags = [];
    private ItemOpeningMode OpeningMode => App.ExplorerPreferences.OpeningMode(Entry.Kind == EntryKind.Directory);

    public FileTile()
    {
        InitializeComponent();
        ItemActivation.BindName(NameText, () => OpeningMode, cursor => ProtectedCursor = cursor);
        Unloaded += (_, _) => FolderPreviewBinder.Clear(FolderPreviewHost, FolderPreviewCover);
        Loaded += (_, _) => BindPreview();
        ResetVisual();
    }

    public int EntryId { get; private set; } = -1;

    public int ViewIndex { get; private set; } = -1;

    public FileEntryCore Entry { get; private set; }

    public void ApplyMetrics(GridSizePreset preset, double itemWidth)
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
        if (Root.Height != preset.ChromeHeight) Root.Height = preset.ChromeHeight;

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
        TagHost.Margin = new Thickness(0, GridSizePreset.TileTagGap, 0, 0);
        TagHost.Height = preset.TagHeight;
        RenderTags();

        var pixels = Math.Clamp((int)Math.Ceiling(preset.IconSize * (XamlRoot?.RasterizationScale ?? 1)), 32, 256);
        if (_previewPixels != pixels)
        {
            _previewPixels = pixels;
            if (_previewPath is not null)
                BindPreview();
        }
    }

    public void Bind(int viewIndex, in FileEntryCore entry, bool selected, string? path)
    {
        ApplyTypography(App.AppearanceViewModel?.Current ?? AppearanceSettings.Default);
        ViewIndex = viewIndex;
        EntryId = entry.Id;
        Entry = entry;
        var prefs = App.ExplorerPreferences;
        NameText.Text = FileRowFormatter.DisplayName(entry, prefs.ShowFileExtensions);
        ToolTipService.SetToolTip(Root, entry.Name);
        Glyph.Glyph = FileRowFormatter.Glyph(entry);
        IconImage.Stretch = FileTypeIconCatalog.Classify(entry) is FileIconKind.Image or FileIconKind.Video
            ? Stretch.UniformToFill : Stretch.Uniform;
        ShellIconBinder.Bind(IconImage, Glyph, entry, path, (int)Math.Round(_preset.IconSize));
        var isFolder = entry.Kind == EntryKind.Directory;
        _previewPath = isFolder ? path : null;
        BindPreview();
        _hidden = FileRowFormatter.IsGhosted(entry.Attributes);
        _selected = selected;
        UpdateState(animate: false);
    }

    internal void ApplyTypography(AppearanceSettings settings)
    {
        FileTypography.Apply(NameText, settings, settings.FileNameFontSize);
        NameText.TextLineBounds = TextLineBounds.Full;
        NameText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
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

    public void Clear()
    {
        ViewIndex = -1;
        EntryId = -1;
        Entry = default;
        NameText.Text = string.Empty;
        ToolTipService.SetToolTip(Root, null);
        Glyph.Glyph = "\uE8B7";
        _tags = [];
        RenderTags();
        IconImage.Stretch = Stretch.Uniform;
        ShellIconBinder.Clear(IconImage, Glyph);
        _previewPath = null;
        FolderPreviewBinder.Clear(FolderPreviewHost, FolderPreviewCover);
        ResetVisual();
    }

    private void BindPreview() =>
        FolderPreviewBinder.Bind(FolderPreviewHost, FolderPreviewCover, _previewPath, _previewPixels);

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
