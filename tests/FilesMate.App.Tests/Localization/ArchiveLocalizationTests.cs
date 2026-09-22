using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FilesMate.App.Localization;

namespace FilesMate.App.Tests.Localization;

public sealed class ArchiveLocalizationTests
{
    [Fact]
    public void Archive_messages_have_matching_arguments_and_resource_mirrors_in_three_languages()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        Assert.NotNull(root);
        var keys = StringTable.Additional.Keys.Where(key => key.StartsWith("Archive_", StringComparison.Ordinal)).ToArray();
        Assert.Contains("Archive_Progress", keys);
        Assert.Contains("Archive_ErrorSourceChanged", keys);
        foreach (var (folder, culture) in new[] { ("en-US", "en-US"), ("zh-Hans", "zh-CN"), ("ja-JP", "ja-JP") })
        {
            var resources = XDocument.Load(Path.Combine(root.FullName, "src", "FilesMate.App", "Strings", folder, "Resources.resw"))
                .Root!.Elements("data").ToDictionary(entry => (string)entry.Attribute("name")!, entry => (string)entry.Element("value")!);
            foreach (var key in keys)
            {
                var text = StringTable.GetForCulture(key, CultureInfo.GetCultureInfo(culture));
                Assert.NotEqual(key, text);
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.Equal(text, resources[key]);
                CompositeFormat.Parse(text);
                var english = StringTable.GetForCulture(key, CultureInfo.GetCultureInfo("en-US"));
                Assert.Equal(Arguments(english), Arguments(text));
            }
        }
        Assert.Equal(new[] { "0", "1", "2", "3" }, Arguments(StringTable.GetForCulture("Archive_Progress", CultureInfo.GetCultureInfo("en-US"))));
    }

    private static string[] Arguments(string text) => Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^{}]*)\}")
        .Select(match => match.Groups[1].Value).Order().ToArray();
}
