using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Icons;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Navigation;

/// <summary>Theme-aware navigation symbols shared by the sidebar and home cards.</summary>
public sealed class PlaceGlyph : UserControl
{
    private readonly FontIcon _icon = new() { FontFamily = new("Segoe Fluent Icons"), FontSize = 16 };
    private readonly Image _image = new()
    {
        Width = FileColumnLayout.DetailsIconSize,
        Height = FileColumnLayout.DetailsIconSize,
        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
        UseLayoutRounding = true,
        Visibility = Visibility.Collapsed,
    };

    public PlaceGlyph()
    {
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        var host = new Grid();
        host.Children.Add(_icon);
        host.Children.Add(_image);
        Content = host;
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => ShellIconBinder.Clear(_image, _icon);
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(PlaceGlyph), new PropertyMetadata("", Changed));

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(string), typeof(PlaceGlyph), new PropertyMetadata(null, Changed));

    public string? Target { get => (string?)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((PlaceGlyph)sender).Refresh();

    private void Refresh()
    {
        // Folder locations share the file list's art, DPI handling and icon preference.
        // Virtual locations and volume roots retain their semantic navigation symbols.
        var folder = Target is { Length: > 0 } path && System.IO.Path.IsPathFullyQualified(path)
            && !string.Equals(System.IO.Path.GetPathRoot(path)?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        _icon.Glyph = folder ? "\uE8B7" : Glyph;
        var tone = _icon.Glyph switch
        {
            "\uE8B7" => "Amber", // Folder / desktop
            "\uE8A5" or "\uE753" => "Blue", // Documents / cloud
            "\uEB9F" or "\uE8B2" => "Violet", // Pictures / video
            "\uE8D6" => "Rose", // Music
            "\uE896" or "\uEDA2" or "\uE88E" or "\uE8EA" or "\uE80F" => "Teal",
            _ => "Neutral"
        };
        _icon.Style = (Style)Application.Current.Resources[$"FilesMate.Place.{tone}Style"];
        if (folder && IsLoaded)
        {
            var entry = new FileEntryCore(0, System.IO.Path.GetFileName(Target!), 0, 0, 0,
                System.IO.FileAttributes.Directory, EntryKind.Directory);
            ShellIconBinder.Bind(_image, _icon, entry, Target, (int)FileColumnLayout.DetailsIconSize);
        }
        else
            ShellIconBinder.Clear(_image, _icon);
    }
}
