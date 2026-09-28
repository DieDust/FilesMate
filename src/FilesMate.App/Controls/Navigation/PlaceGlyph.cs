using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Navigation;

/// <summary>Theme-aware navigation symbols shared by the sidebar and home cards.</summary>
public sealed class PlaceGlyph : UserControl
{
    private readonly FontIcon _icon = new() { FontFamily = new("Segoe Fluent Icons"), FontSize = 16 };

    public PlaceGlyph() => Content = _icon;

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(PlaceGlyph), new PropertyMetadata("", Changed));

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (PlaceGlyph)sender;
        control._icon.Glyph = (string)args.NewValue;
        var tone = control.Glyph switch
        {
            "\uE8B7" => "Amber", // Folder / desktop
            "\uE8A5" or "\uE753" => "Blue", // Documents / cloud
            "\uEB9F" or "\uE8B2" => "Violet", // Pictures / video
            "\uE8D6" => "Rose", // Music
            "\uE896" or "\uEDA2" or "\uE88E" or "\uE8EA" or "\uE80F" => "Teal",
            _ => "Neutral"
        };
        control._icon.Style = (Style)Application.Current.Resources[$"FilesMate.Place.{tone}Style"];
    }
}
