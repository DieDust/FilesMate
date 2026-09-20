using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FilesMate.App.Localization;

namespace FilesMate.App.Tests.Localization;

public sealed class LanguageCoverageTests
{
    [Fact]
    public void Windows_resource_mirrors_match_shared_runtime_translations()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (var (folder, culture) in new[] { ("en-US", "en-US"), ("zh-Hans", "zh-CN"), ("ja-JP", "ja-JP") })
        {
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(root.FullName, "src", "FilesMate.App", "Strings", folder, "Resources.resw"));
            var values = document.Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);
            var keys = StringTable.English.Keys.Concat(StringTable.Additional.Keys).Order().ToArray();
            Assert.Equal(keys, values.Keys.Order());
            foreach (var key in keys) Assert.Equal(StringTable.GetForCulture(key, CultureInfo.GetCultureInfo(culture)), values[key]);
        }
    }

    [Fact]
    public void Three_languages_have_the_same_keys_and_format_arguments()
    {
        Assert.Equal(StringTable.English.Keys.Order(), StringTable.Chinese.Keys.Order());
        Assert.Equal(StringTable.English.Keys.Order(), StringTable.Japanese.Keys.Order());
        Assert.Empty(StringTable.Additional.Keys.Intersect(StringTable.English.Keys));
        foreach (var key in StringTable.English.Keys.Concat(StringTable.Additional.Keys))
        {
            var values = new[] { "en-US", "zh-CN", "ja-JP" }
                .Select(language => StringTable.GetForCulture(key, CultureInfo.GetCultureInfo(language))).ToArray();
            Assert.All(values, value => Assert.False(string.IsNullOrWhiteSpace(value), key));
            var arguments = values.Select(value => Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[^{}]*)\}")
                .Select(match => match.Groups[1].Value).Order().ToArray()).ToArray();
            Assert.True(arguments[0].SequenceEqual(arguments[1]), $"Chinese arguments: {key}");
            Assert.True(arguments[0].SequenceEqual(arguments[2]), $"Japanese arguments: {key}");
            foreach (var value in values) CompositeFormat.Parse(value);
        }
    }

    [Theory]
    [InlineData("ja-JP", "設定")]
    [InlineData("ja", "設定")]
    [InlineData("en-GB", "Settings")]
    [InlineData("zh-SG", "设置")]
    [InlineData("fr-FR", "Settings")]
    public void Culture_families_and_unsupported_languages_resolve_predictably(string culture, string expected)
    {
        Assert.Equal(expected, StringTable.GetForCulture("Settings", CultureInfo.GetCultureInfo(culture)));
        Assert.Equal("Missing_Key", StringTable.GetForCulture("Missing_Key", CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void Language_selection_round_trips_without_changing_other_settings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FilesMateLanguageTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "language.json");
        try
        {
            Assert.Equal("System", LanguageSettings.Load(path));
            foreach (var language in new[] { "en-US", "zh-CN", "ja-JP", "System" })
            {
                LanguageSettings.Save(path, language);
                Assert.Equal(language, LanguageSettings.Load(path));
                Assert.Single(Directory.GetFiles(folder));
            }
            File.WriteAllText(path, "{ broken json");
            Assert.Equal("System", LanguageSettings.Load(path));
            LanguageSettings.Save(path, "unsupported");
            Assert.Equal("System", LanguageSettings.Load(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
