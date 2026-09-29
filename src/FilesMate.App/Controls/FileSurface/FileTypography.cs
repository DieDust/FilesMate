using FilesMate.App.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

internal static class FileTypography
{
    internal static void Apply(TextBlock text, AppearanceSettings settings, double size)
    {
        text.UseLayoutRounding = true;
        text.FontSize = size;
        text.TextLineBounds = Microsoft.UI.Xaml.TextLineBounds.Tight;
        if (settings.FileFontFamily is { } family) text.FontFamily = new FontFamily(family);
        else text.ClearValue(TextBlock.FontFamilyProperty);
    }
}
