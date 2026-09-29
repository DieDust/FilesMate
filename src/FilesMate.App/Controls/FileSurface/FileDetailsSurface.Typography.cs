using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private AppearanceSettings _typography = AppearanceSettings.Default;
    private XamlRoot? _typographyRoot;
    private double _typographyScale = 1;
    private double RowHeight => RowTypography.FileRowHeightForScale(XamlRoot?.RasterizationScale ?? 1);
    private GridSizePreset EffectiveGridPreset => _gridPreset.WithFontSize(_typography.FileNameFontSize);

    private void TypographyChanged(object? sender, AppearanceSettings settings)
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(() => TypographyChanged(sender, settings)); return; }
        _typography = settings;
        UseLayoutRounding = true;
        _typographyScale = XamlRoot?.RasterizationScale ?? 1;
        foreach (var row in _realized) row.ApplyTypography(RowTypography, _typographyScale);
        foreach (var tile in _tiles) { tile.ApplyTypography(settings); tile.ApplyMetrics(EffectiveGridPreset, GridItemWidth()); }
        foreach (var (button, _) in _columnHeaders.Values) ApplyHeaderTypography(button);
        HeaderRow.Height = new GridLength(_layout == FileLayoutKind.Details ? RowHeight : 0);
        ApplyGridMetrics(); UpdateListMetrics(); Repeater.InvalidateMeasure();
    }

    private void StartTypography()
    {
        App.AppearanceChanged -= TypographyChanged;
        App.AppearanceChanged += TypographyChanged;
        _typographyRoot = XamlRoot;
        if (_typographyRoot is not null) _typographyRoot.Changed += TypographyRootChanged;
        TypographyChanged(null, App.AppearanceViewModel?.Current ?? AppearanceSettings.Default);
    }
    private void StopTypography()
    {
        App.AppearanceChanged -= TypographyChanged;
        if (_typographyRoot is not null) _typographyRoot.Changed -= TypographyRootChanged;
        _typographyRoot = null;
    }
    private void TypographyRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale != _typographyScale) TypographyChanged(null, _typography);
    }

    private void ApplyHeaderTypography(Button button)
    {
        button.Height = Math.Max(24, RowHeight - 4);
        if (button.Content is Panel panel)
            foreach (var text in panel.Children.OfType<TextBlock>())
                FileTypography.Apply(text, _typography, _typography.FileDetailsFontSize);
    }
}
