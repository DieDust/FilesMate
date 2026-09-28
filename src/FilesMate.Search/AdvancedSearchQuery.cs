using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FilesMate.Search;

/// <summary>Filename/metadata predicates only. Never opens result files during a query.</summary>
public sealed class AdvancedSearchQuery
{
    private sealed record Term(string Value, bool Negated);
    private readonly List<List<Func<AdvancedSearchHit, bool>>> _groups = [];
    private readonly List<Func<AdvancedSearchHit, bool>> _constraints = [];
    public bool RequiresMetadata { get; private set; }
    public IReadOnlyList<string> IndexTerms { get; private set; } = [];
    public bool Matches(AdvancedSearchHit hit) => _constraints.All(p => p(hit)) && _groups.Any(g => g.All(p => p(hit)));

    public static AdvancedSearchQuery Parse(SearchPageRequest request, DateTime? today = null)
    {
        if (request.Query.Length > 2048) throw new ArgumentException("SearchPage_QueryTooLong");
        var result = new AdvancedSearchQuery();
        var comparison = request.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var literals = new List<string>();
        var now = (today ?? DateTime.Today).Date;
        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            var scope = NormalizeScope(request.Scope);
            result._constraints.Add(h => InScope(h.Path, scope));
        }
        if (request.Extensions.Trim().Length > 0) result._constraints.Add(ExtensionPredicate(request.Extensions));
        if (request.Size.Trim().Length > 0) { result._constraints.Add(SizePredicate(request.Size)); result.RequiresMetadata = true; }
        if (request.Modified.Trim().Length > 0) { result._constraints.Add(DatePredicate(request.Modified, now)); result.RequiresMetadata = true; }
        if (request.Regex)
        {
            var regex = SafeRegex(request.Query, request.MatchCase);
            result._groups.Add([h => regex.IsMatch(request.MatchPath ? h.Path : h.Name)]);
            return result;
        }
        var group = new List<Func<AdvancedSearchHit, bool>>();
        result._groups.Add(group);
        foreach (var term in Tokenize(request.Query))
        {
            if (term.Value == "|")
            {
                if (group.Count == 0) throw new ArgumentException("SearchPage_OrNeedsTerms");
                group = []; result._groups.Add(group); continue;
            }
            var value = term.Value;
            Func<AdvancedSearchHit, bool> predicate;
            var colon = value.IndexOf(':');
            var key = colon > 1 ? value[..colon].ToLowerInvariant() : "";
            var arg = colon > 1 ? value[(colon + 1)..] : value;
            switch (key)
            {
                case "file": case "folder":
                    var directory = key == "folder";
                    var nameMatch = TextPredicate(arg, comparison, true);
                    predicate = h => h.IsDirectory == directory && nameMatch(h.Name);
                    break;
                case "ext": predicate = ExtensionPredicate(arg); break;
                case "path":
                    var pathMatch = TextPredicate(arg, comparison, false);
                    predicate = h => pathMatch(h.Path); break;
                case "parent":
                    var parent = NormalizeScope(arg);
                    predicate = h => string.Equals(Path.GetDirectoryName(h.Path)?.TrimEnd('\\', '/'), parent.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
                    break;
                case "size": predicate = SizePredicate(arg); result.RequiresMetadata = true; break;
                case "dm": predicate = DatePredicate(arg, now); result.RequiresMetadata = true; break;
                default:
                    if (key.Length > 0) throw new ArgumentException("SearchPage_UnsupportedCondition");
                    // An absolute drive/folder token is a scope, not a filename.
                    if (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':' || value.StartsWith("\\\\", StringComparison.Ordinal))
                    {
                        var scope = NormalizeScope(value.Length == 2 ? value + "\\" : value);
                        predicate = h => InScope(h.Path, scope);
                    }
                    else
                    {
                        var match = TextPredicate(value, comparison, true);
                        predicate = h => match(request.MatchPath ? h.Path : h.Application?.SearchText ?? h.Name);
                        if (!term.Negated && !request.MatchPath && value.IndexOfAny(['*', '?']) < 0) literals.Add(value);
                    }
                    break;
            }
            group.Add(term.Negated ? h => !predicate(h) : predicate);
        }
        if (result._groups.Count > 1 && group.Count == 0) throw new ArgumentException("SearchPage_OrNeedsTerms");
        // Every positive term is required only in an AND-only query. OR and regex
        // deliberately skip this narrowing, so the index cannot hide valid hits.
        if (result._groups.Count == 1) result.IndexTerms = literals;
        return result;
    }

    public static string NormalizeScope(string value)
    {
        value = value.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("SearchPage_InvalidScope");
        return Path.GetFullPath(value).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
    }
    private static bool InScope(string path, string prefix) => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Term> Tokenize(string text)
    {
        var token = new StringBuilder(); bool quoted = false; bool negated = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (!quoted && (char.IsWhiteSpace(c) || c == '|'))
            {
                if (token.Length > 0) { yield return new(token.ToString(), negated); token.Clear(); negated = false; }
                else if (negated) throw new ArgumentException("SearchPage_ExcludeNeedsTerm");
                if (c == '|') yield return new("|", false);
            }
            else if (!quoted && token.Length == 0 && c == '!') negated = true;
            else token.Append(c);
        }
        if (quoted) throw new ArgumentException("SearchPage_UnclosedQuote");
        if (token.Length > 0) yield return new(token.ToString(), negated);
        else if (negated) throw new ArgumentException("SearchPage_ExcludeNeedsTerm");
    }

    private static Regex SafeRegex(string pattern, bool matchCase)
    {
        try { return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking |
            (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(50)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { throw new ArgumentException("SearchPage_InvalidRegex", e); }
    }
    private static Func<string, bool> TextPredicate(string text, StringComparison comparison, bool wildcard)
    {
        if (!wildcard || text.IndexOfAny(['*', '?']) < 0) return candidate => candidate.Contains(text, comparison);
        var pattern = "^" + Regex.Escape(text).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var regex = SafeRegex(pattern, comparison == StringComparison.Ordinal);
        return regex.IsMatch;
    }
    private static Func<AdvancedSearchHit, bool> ExtensionPredicate(string text)
    {
        string[] extensions;
        try { extensions = SearchCategories.ParseExtensions(text); }
        catch (ArgumentException error) { throw new ArgumentException("Category_InvalidExtensions", error); }
        return h => !h.IsDirectory && extensions.Contains(Path.GetExtension(h.Path), StringComparer.OrdinalIgnoreCase);
    }
    private static long Bytes(string text)
    {
        var match = Regex.Match(text.Trim(), @"^(\d+(?:\.\d+)?)\s*(b|kb|mb|gb|tb)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) throw new ArgumentException("SearchPage_InvalidSize");
        var power = match.Groups[2].Value.ToLowerInvariant() switch { "kb" => 1, "mb" => 2, "gb" => 3, "tb" => 4, _ => 0 };
        var multiplier = (decimal)Math.Pow(1024, power);
        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number > long.MaxValue / multiplier) throw new ArgumentException("SearchPage_SizeTooLarge");
        return (long)(number * multiplier);
    }
    private static Func<AdvancedSearchHit, bool> SizePredicate(string text)
    {
        var range = text.Trim().Split("..", StringSplitOptions.None);
        if (range.Length == 2)
        {
            var min = Bytes(range[0]); var max = Bytes(range[1]);
            if (min > max) throw new ArgumentException("SearchPage_SizeOrder");
            return h => !h.IsDirectory && h.Size is { } n && n >= min && n <= max;
        }
        var op = text.TrimStart(); var prefix = op.StartsWith(">=") || op.StartsWith("<=") ? op[..2] : op.StartsWith('>') || op.StartsWith('<') || op.StartsWith('=') ? op[..1] : "";
        var size = Bytes(op[prefix.Length..]);
        return h => !h.IsDirectory && h.Size is { } n && (prefix switch { ">" => n > size, ">=" => n >= size, "<" => n < size, "<=" => n <= size, _ => n == size });
    }
    private static Func<AdvancedSearchHit, bool> DatePredicate(string text, DateTime today)
    {
        text = text.Trim().ToLowerInvariant();
        DateTime start, end;
        if (text == "today") { start = today; end = today.AddDays(1); }
        else if (text == "yesterday") { start = today.AddDays(-1); end = today; }
        else if (text.EndsWith("days") && int.TryParse(text[..^4], out var days) && days is > 0 and <= 36500)
        { start = today.AddDays(1 - days); end = today.AddDays(1); }
        else
        {
            var range = text.Split("..", StringSplitOptions.None);
            DateTime Date(string value) => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                && d < DateTime.MaxValue.Date ? d : throw new ArgumentException("SearchPage_InvalidDate");
            if (range.Length > 2) throw new ArgumentException("SearchPage_DateRange");
            start = Date(range[0]); end = Date(range[^1]).AddDays(1);
            if (end <= start) throw new ArgumentException("SearchPage_DateOrder");
        }
        var min = DateTime.SpecifyKind(start, DateTimeKind.Local).ToUniversalTime().Ticks;
        var max = DateTime.SpecifyKind(end, DateTimeKind.Local).ToUniversalTime().Ticks;
        return h => h.ModifiedUtcTicks is { } ticks && ticks >= min && ticks < max;
    }
}

public sealed record AdvancedSearchHit(string Name, string Path, bool IsDirectory, long? Size = null,
    long? ModifiedUtcTicks = null, ApplicationEntry? Application = null);
public sealed record AdvancedSearchResponse(IReadOnlyList<AdvancedSearchHit> Hits, bool HasMore, string? Notice = null, long? TotalCount = null);
