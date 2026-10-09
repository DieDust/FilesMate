using FilesMate.App.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

internal static class FileTypography
{
    internal static void Apply(TextBlock text, AppearanceSettings settings, double size)
    {
        if (!text.UseLayoutRounding) text.UseLayoutRounding = true;
        if (text.FontSize != size) text.FontSize = size;
        if (text.TextLineBounds != Microsoft.UI.Xaml.TextLineBounds.Tight) text.TextLineBounds = Microsoft.UI.Xaml.TextLineBounds.Tight;
        if (settings.FileFontFamily is { } family)
        {
            if (text.FontFamily.Source != family) text.FontFamily = new FontFamily(family);
        }
        else if (text.ReadLocalValue(TextBlock.FontFamilyProperty) != Microsoft.UI.Xaml.DependencyProperty.UnsetValue)
            text.ClearValue(TextBlock.FontFamilyProperty);
    }
}
