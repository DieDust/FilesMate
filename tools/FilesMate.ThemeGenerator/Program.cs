using System.Xml.Linq;
using FilesMate.App.Models;

var path = Path.GetFullPath(args.Length > 0 ? args[0] : "src/FilesMate.App/Themes/AppThemeResources.xaml");
var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
foreach (var dark in new[] { false, true })
{
    var name = dark ? "Dark" : "Light";
    var theme = document.Descendants().Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(x + "Key") == name);
    var resources = theme.Elements().Concat(document.Root!.Elements()).Where(e => e.Attribute(x + "Key") != null)
        .ToDictionary(e => (string)e.Attribute(x + "Key")!, StringComparer.Ordinal);
    foreach (var (key, color) in SkinPalette.For(dark).ResourceColors())
    {
        if (!resources.TryGetValue(key, out var element)) throw new InvalidDataException($"Missing skin resource: {name}/{key}");
        if (element.Name.LocalName == "Color") element.Value = AccentPalette.ToHex(color);
        else if (element.Name.LocalName == "AcrylicBrush")
        { element.SetAttributeValue("TintColor", AccentPalette.ToHex(color)); element.SetAttributeValue("FallbackColor", AccentPalette.ToHex(color)); }
        else if (element.Name.LocalName == "SolidColorBrush") element.SetAttributeValue("Color", AccentPalette.ToHex(color));
        else throw new InvalidDataException($"Unexpected skin resource: {key}/{element.Name}");
    }
    // Decorative sheen uses the same light-facing ink as the rest of the skin.
    var sheen = resources["FilesMate.LiquidGlass.TopSheenBrush"];
    foreach (var stop in sheen.Elements())
    {
        var original = (string)stop.Attribute("Color")!;
        var alpha = original.Length == 9 ? Convert.ToByte(original.Substring(1, 2), 16) : (byte)255;
        stop.SetAttributeValue("Color", AccentPalette.ToHex(SkinPalette.Alpha(dark ? SkinPalette.DarkSkin.Text : SkinPalette.Light.Card, alpha)));
    }
}
document.Save(path, SaveOptions.DisableFormatting);
Console.WriteLine($"Synchronized skin resources: {path}");
var companionPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../FilesMate.SearchHost/PaletteWindow.xaml"));
var companion = XDocument.Load(companionPath, LoadOptions.PreserveWhitespace);
foreach (var (key, color) in SkinPalette.DarkSkin.CompanionColors())
    companion.Descendants().Single(e => (string?)e.Attribute(x + "Key") == key).SetAttributeValue("Color", AccentPalette.ToHex(color));
companion.Save(companionPath, SaveOptions.DisableFormatting);
Console.WriteLine($"Synchronized companion fallbacks: {companionPath}");
