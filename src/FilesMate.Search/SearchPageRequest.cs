using System.Text;
using System.Text.Json;

namespace FilesMate.Search;

public enum SearchResultSort { Priority, Name, NameDescending, ModifiedDescending, SizeDescending, Path }

/// <summary>A serializable search, shared by the palette, tabs and session restore.</summary>
public sealed record SearchPageRequest(string Query = "", string? Scope = null, string CategoryId = "All",
    string Extensions = "", string Size = "", string Modified = "", bool MatchCase = false,
    bool MatchPath = false, bool Regex = false, SearchResultSort Sort = SearchResultSort.Priority)
{
    public const string Prefix = "filesmate-search:";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Location => Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));

    public static bool TryParse(string? location, out SearchPageRequest request)
    {
        request = new();
        if (location is null || !location.StartsWith(Prefix, StringComparison.Ordinal) || location.Length > 16384) return false;
        try
        {
            var value = JsonSerializer.Deserialize<SearchPageRequest>(Convert.FromBase64String(location[Prefix.Length..]));
            if (value is null || value.Query is null || value.Query.Length > 2048 || value.Scope?.Length > 4096 ||
                value.Extensions is null || value.Extensions.Length > 1024 || value.Size is null || value.Size.Length > 100 ||
                value.Modified is null || value.Modified.Length > 100 || value.CategoryId is null || value.CategoryId.Length > 64 ||
                !Enum.IsDefined(value.Sort)) return false;
            request = value;
            return true;
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException) { return false; }
    }
}
