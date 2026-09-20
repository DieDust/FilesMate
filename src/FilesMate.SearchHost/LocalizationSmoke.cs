#if FILESMATE_UI_TEST
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FilesMate.App.Localization;

namespace FilesMate.SearchHost;

internal sealed partial class SearchHost
{
    internal async Task RunLocalizationSmokeAsync()
    {
        _window ??= new PaletteWindow(this, _searchProvider);
        await _window.RunLocalizationSmokeAsync();
    }
}

public partial class PaletteWindow
{
    internal async Task RunLocalizationSmokeAsync()
    {
        var language = CultureInfo.CurrentUICulture.Name;
        var cases = new List<object>();
        var path = Path.Combine(_host.Profile, $"search-localization-{language}.json");
        try
        {
            Open(true, "");
            Topmost = false;
            Left = -10000;
            Top = -10000;
            await Task.Delay(600);
            foreach (var section in new[] { "settings", "categories", "ranking", "search" })
            {
                ShowSettings(section != "search", "");
                if (section == "categories") Categories_Open(this, new RoutedEventArgs());
                if (section == "ranking") Ranking_Click(this, new RoutedEventArgs());
                await Task.Delay(250);
                UpdateLayout();
                var texts = Descendants(this).OfType<TextBlock>().Where(e => e.IsVisible && e.ActualWidth > 0).Select(e => e.Text).ToArray();
                if (texts.Length < 2) throw new InvalidOperationException($"Empty screen: {section}");
                var untranslated = texts.Where(t => Regex.IsMatch(t, "[一-龥]") && !StringTable.English.Values.Contains(t)
                    && !StringTable.Additional.Values.Any(e => e.English == t)).ToArray();
                if (language.StartsWith("en") && untranslated.Length > 0)
                    throw new InvalidOperationException("Untranslated text: " + string.Join(" | ", untranslated));
                cases.Add(new { Section = section, Texts = texts });
            }
            File.WriteAllText(path, JsonSerializer.Serialize(new { Passed = true, Language = language, Cases = cases }));
        }
        catch (Exception error)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { Passed = false, Language = language, Error = error.ToString(), Cases = cases }));
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
#endif
