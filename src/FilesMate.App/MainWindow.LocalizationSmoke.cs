#if FILESMATE_UI_TEST
using System.Globalization;
using System.Text.Json;
using FilesMate.App.Controls.Settings;
using FilesMate.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunLocalizationSmokeAsync()
    {
        var language = CultureInfo.CurrentUICulture.Name;
        var result = Path.Combine(AppContext.BaseDirectory, $"localization-{language}.json");
        var cases = new List<object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            await Task.Delay(500);
            foreach (var width in new[] { 1900, 2500 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 1300));
                foreach (var section in new[] { "general", "appearance", "files-folders", "search", "keyboard", "tags", "advanced", "about" })
                {
                    OpenSettings(section);
                    await Task.Delay(600);
                    ((FrameworkElement)Content).UpdateLayout();
                    if (_settingsPage is null) throw new InvalidOperationException("Settings did not load.");
                    var elements = Descendants(_settingsPage).OfType<FrameworkElement>().ToArray();
                    if (elements.Any(e => e.GetType().Name == "PageLoadErrorPage"))
                        throw new InvalidOperationException($"Page failed: {section}");
                    foreach (var card in elements.OfType<SettingCard>().Where(c => c.ActualWidth > 0))
                    {
                        if (card.Action is FrameworkElement action && action.ActualWidth > 0)
                        {
                            var rect = action.TransformToVisual(card).TransformBounds(new Windows.Foundation.Rect(0, 0, action.ActualWidth, action.ActualHeight));
                            if (rect.X < -1 || rect.Right > card.ActualWidth + 1)
                                throw new InvalidOperationException($"Action overflow: {section}/{card.Title}: {rect.Right} > {card.ActualWidth}");
                        }
                    }
                    if (section == "general")
                    {
                        var languageBox = elements.OfType<ComboBox>().Single(e => e.Name == "LanguageBox");
                        if ((languageBox.SelectedItem as ComboBoxItem)?.Tag as string != language)
                            throw new InvalidOperationException("Language selection did not restore.");
                        if (!elements.OfType<TextBlock>().Any(e => e.Text == StringTable.Get("Language_Title")))
                            throw new InvalidOperationException("Language card was not translated.");
                    }
                    var texts = elements.OfType<TextBlock>().Where(e => e.ActualWidth > 0).Select(e => e.Text).ToArray();
                    var untranslated = texts.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t ?? "", "[一-龥]")
                        && (StringTable.Chinese.Values.Contains(t) || StringTable.Additional.Values.Any(entry => entry.Chinese == t))
                        && !StringTable.English.Values.Contains(t) && !StringTable.Additional.Values.Any(entry => entry.English == t)).ToArray();
                    if (language.StartsWith("en") && untranslated.Length > 0)
                        throw new InvalidOperationException($"Untranslated Chinese in {section}: " + string.Join(" | ", untranslated));
                    cases.Add(new { width, section, Texts = texts });
                }
            }
            File.WriteAllText(result, JsonSerializer.Serialize(new { Passed = true, Language = language, Cases = cases }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(result, JsonSerializer.Serialize(new { Passed = false, Language = language, Error = error.ToString(), Cases = cases }));
        }
        finally { CloseSettings(); }

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
