using System.Xml.Linq;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class ControlThemeContrastTests
{
    [Fact]
    public void Common_controls_define_the_same_keys_in_every_theme()
    {
        var themes = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"));
        foreach (var theme in new[] { "Light", "Dark", "HighContrast" })
        foreach (var key in new[] { "ContentDialogBackground", "ContentDialogTopOverlay", "ContentDialogForeground",
            "TextControlHeaderForeground", "ComboBoxHeaderForeground", "CheckBoxForegroundUnchecked",
            "ButtonForeground", "TextFillColorSecondaryBrush", "SystemFillColorCriticalBrush",
            "ListViewItemForeground", "TextControlButtonForeground", "TextControlBackground" })
            Assert.Contains(key, ThemeXaml.Keys(themes[theme]));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Dialog_text_headers_and_errors_remain_readable(string theme)
    {
        var dictionary = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"))[theme];
        var entries = dictionary.Elements().ToDictionary(e => (string)e.Attribute(ThemeXaml.Xaml + "Key")!);
        uint Color(string key)
        {
            var element = entries[key];
            if (element.Attribute("ResourceKey") is XAttribute target) return Color(target.Value);
            return Convert.ToUInt32(((string)element.Attribute("Color")!)[1..], 16);
        }
        static double Luminance(uint foreground, uint background)
        {
            var alpha = (foreground >> 24) / 255d;
            double Channel(int shift)
            {
                var value = (((foreground >> shift) & 255) * alpha + ((background >> shift) & 255) * (1 - alpha)) / 255d;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(16) + .7152 * Channel(8) + .0722 * Channel(0);
        }
        foreach (var key in new[] { "ContentDialogForeground", "TextControlHeaderForeground", "ComboBoxHeaderForeground", "CheckBoxForegroundUnchecked", "TextFillColorSecondaryBrush", "SystemFillColorCriticalBrush" })
        {
            var background = Color("ContentDialogTopOverlay");
            var a = Luminance(Color(key), background);
            var b = Luminance(background, background);
            Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5, $"{theme}: {key} is not readable.");
        }
    }
}
