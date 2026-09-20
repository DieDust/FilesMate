#if FILESMATE_UI_TEST
using System.Windows.Media.Imaging;
using FilesMate.Search;

namespace FilesMate.SearchHost;

internal static class IconStyleSmoke
{
    internal static async Task RunAsync(string profile)
    {
        Directory.CreateDirectory(profile);
        try
        {
            var path = Path.Combine(profile, "associated.pdf");
            File.WriteAllText(path, "icon-only fixture");
            var icons = new SearchResultIcons();
            var hit = new NameHit("associated.pdf", path, false, null);
            var bundled = icons.Fallback(hit);
            var row = new SearchRow(hit, bundled);
            await icons.LoadAsync(row, CancellationToken.None);
            if (!ReferenceEquals(row.Icon, bundled)) throw new InvalidOperationException("Bundled icon changed unexpectedly.");
            icons.UseBundledIcons = false;
            row.Icon = icons.Fallback(hit);
            await icons.LoadAsync(row, CancellationToken.None);
            if (ReferenceEquals(row.Icon, bundled) || row.Icon is not BitmapSource { PixelWidth: >= 16 })
                throw new InvalidOperationException("Windows association icon was not loaded.");
            icons.UseBundledIcons = true;
            row.Icon = icons.Fallback(hit);
            await icons.LoadAsync(row, CancellationToken.None);
            if (!ReferenceEquals(row.Icon, bundled)) throw new InvalidOperationException("Restoring bundled style failed.");
            File.WriteAllText(Path.Combine(profile, "appearance.json"), "{\"useBundledFileIcons\":false}");
            if (PaletteAppearance.Load(profile).UseBundledFileIcons) throw new InvalidOperationException("Saved icon preference ignored.");
            File.WriteAllText(Path.Combine(profile, "appearance.json"), "{}");
            if (!PaletteAppearance.Load(profile).UseBundledFileIcons) throw new InvalidOperationException("Existing profile default changed.");
            File.WriteAllText(Path.Combine(profile, "icon-style-smoke.txt"), "Passed");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(profile, "icon-style-smoke.txt"), error.ToString());
            Environment.ExitCode = 1;
        }
    }
}
#endif
