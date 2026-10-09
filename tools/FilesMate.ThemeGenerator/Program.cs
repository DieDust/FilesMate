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
        if (!resources.TryGetValue(key, out var element))
        {
            if (!key.StartsWith("FilesMate.", StringComparison.Ordinal))
                throw new InvalidDataException($"Missing skin resource: {name}/{key}");
            element = new XElement(theme.Name.Namespace + "SolidColorBrush", new XAttribute(x + "Key", key));
            theme.Add(element);
            resources.Add(key, element);
        }
        if (element.Name.LocalName == "StaticResource")
        {
            // A state role may need paired ink independent of the primary text alias.
            var owned = new XElement(theme.Name.Namespace + "SolidColorBrush", new XAttribute(x + "Key", key));
            element.ReplaceWith(owned);
            resources[key] = element = owned;
        }
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
var highContrast = document.Descendants().Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(x + "Key") == "HighContrast");
foreach (var key in SkinPalette.Light.ResourceColors().Keys.Where(key =>
    key.StartsWith("FilesMate.Choice.", StringComparison.Ordinal) || key.StartsWith("FilesMate.NameMatch.", StringComparison.Ordinal)
    || key.StartsWith("FilesMate.Menu.ItemSelected", StringComparison.Ordinal) || key == "FilesMate.FileItem.HoverForegroundBrush"))
{
    if (highContrast.Elements().Any(e => (string?)e.Attribute(x + "Key") == key)) continue;
    highContrast.Add(new XElement(highContrast.Name.Namespace + "StaticResource", new XAttribute(x + "Key", key),
        new XAttribute("ResourceKey", key.Contains("Foreground", StringComparison.Ordinal) ? "SystemColorHighlightTextColorBrush" : "SystemColorHighlightColorBrush")));
}
document.Save(path, SaveOptions.DisableFormatting);
Console.WriteLine($"Synchronized skin resources: {path}");
var companionPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../FilesMate.SearchHost/PaletteWindow.xaml"));
var companion = XDocument.Load(companionPath, LoadOptions.PreserveWhitespace);
foreach (var (key, color) in SkinPalette.DarkSkin.CompanionColors())
    companion.Descendants().Single(e => (string?)e.Attribute(x + "Key") == key).SetAttributeValue("Color", AccentPalette.ToHex(color));
companion.Save(companionPath, SaveOptions.DisableFormatting);
Console.WriteLine($"Synchronized companion fallbacks: {companionPath}");
