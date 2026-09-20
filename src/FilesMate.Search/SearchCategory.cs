using System.Text.Json;
using System.Text.RegularExpressions;

namespace FilesMate.Search;

public sealed record SearchCategory(string Id, string Name, SearchFilter? Builtin, string[] Extensions, bool Visible = true);

public static class SearchCategories
{
    public static List<SearchCategory> Defaults() => new[] { "全部", "应用", "文档", "图片", "影音", "文件夹" }
        .Select((name, index) => new SearchCategory(((SearchFilter)index).ToString(), name, (SearchFilter)index, [])).ToList();

    public static string[] ParseExtensions(string text)
    {
        var tokens = text.Split([' ', ',', ';', '，', '；', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length is 0 or > 64) throw new ArgumentException("请输入 1–64 个后缀，例如 jpg, png。");
        return tokens.Select(token =>
        {
            var suffix = token.TrimStart('*', '.').ToLowerInvariant();
            if (!Regex.IsMatch(suffix, "^[a-z0-9][a-z0-9_+-]{0,19}$", RegexOptions.CultureInvariant))
                throw new ArgumentException("后缀请填写 jpg、png 这样的格式，不包含路径或通配符。");
            return "." + suffix;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static List<SearchCategory> Load(string profile)
    {
        try
        {
            var path = Path.Combine(profile, "search-categories.json");
            if (new FileInfo(path).Length > 64 * 1024) return Defaults();
            return Normalize(JsonSerializer.Deserialize<List<SearchCategory>>(File.ReadAllText(path)) ?? []);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return Defaults(); }
    }

    public static List<SearchCategory> Normalize(IEnumerable<SearchCategory> source)
    {
        var result = new List<SearchCategory>();
        foreach (var category in source.Take(32))
        {
            if (category is null || string.IsNullOrWhiteSpace(category.Id) || result.Any(c => c.Id == category.Id)) continue;
            if (category.Builtin is { } builtin)
            {
                if (!Enum.IsDefined(builtin) || result.Any(c => c.Builtin == builtin)) continue;
                result.Add(Defaults().Single(c => c.Builtin == builtin) with { Visible = category.Visible });
            }
            else if (!string.IsNullOrWhiteSpace(category.Name) && category.Name.Length <= 24)
                result.Add(category with { Extensions = ParseExtensions(string.Join(",", category.Extensions ?? [])) });
        }
        foreach (var item in Defaults()) if (result.All(c => c.Builtin != item.Builtin)) result.Add(item with { Visible = false });
        if (result.All(c => !c.Visible)) result[0] = result[0] with { Visible = true };
        return result;
    }

    public static void Save(string profile, IEnumerable<SearchCategory> source)
    {
        var normalized = Normalize(source);
        Directory.CreateDirectory(profile);
        var path = Path.Combine(profile, "search-categories.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(normalized)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
