using System.IO;

using FilesMate.App.Localization;

namespace FilesMate.App.Navigation;

public static class LocationCaption
{
    public const string HomeGlyph = "\uE80F";
    public const string TagGlyph = "\uE8EC";

    public static string Title(string? path, string? tagName = null)
    {
        if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device)) return device.Name;
        if (HomeLocation.IsHome(path))
        {
            return StringTable.Get("Home");
        }

        if (TagLocation.IsTag(path))
        {
            return string.IsNullOrWhiteSpace(tagName) ? StringTable.Get("Nav_Tags") : tagName.Trim();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public static string? Glyph(string? path)
    {
        if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out _)) return "\uE8EA";
        if (HomeLocation.IsHome(path))
        {
            return HomeGlyph;
        }

        if (TagLocation.IsTag(path))
        {
            return TagGlyph;
        }

        return CloudLocation.IsRoot(path) ? CloudLocation.Glyph : null;
    }
}
