using System.Text.RegularExpressions;

namespace FilesMate.Core.Operations;

public static class BatchRenamePlanner
{
    private const string InvalidCharacters = "<>:\"/\\|?*";

    public static BatchRenamePlan Plan(
        IEnumerable<string> sources,
        BatchRenameRule rule,
        Func<string, bool>? pathExists = null,
        Func<string, bool>? isDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(rule);
        var paths = sources
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return new BatchRenamePlan([]);
        }

        Regex? regex = null;
        string? regexError = null;
        if (!string.IsNullOrWhiteSpace(rule.RegexPattern))
        {
            try
            {
                regex = new Regex(rule.RegexPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException error)
            {
                regexError = $"Invalid regular expression: {error.Message}";
            }
        }

        var sourceSet = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<BatchRenameEntry>(paths.Length);
        for (var i = 0; i < paths.Length; i++)
        {
            var source = paths[i];
            var parent = Path.GetDirectoryName(source);
            var currentName = Path.GetFileName(source);
            var directory = isDirectory?.Invoke(source) == true;
            var stem = directory ? currentName : Path.GetFileNameWithoutExtension(source);
            var extension = directory ? string.Empty : Path.GetExtension(source);
            var name = rule.BaseName ?? (rule.IncludeExtension ? currentName : stem);
            var error = regexError;

            if (error is null)
            {
                if (regex is not null)
                {
                    try { name = regex.Replace(name, rule.RegexReplacement); }
                    catch (RegexMatchTimeoutException) { error = "The regular expression took too long."; }
                }
                else if (!string.IsNullOrEmpty(rule.Find))
                {
                    name = name.Replace(rule.Find, rule.Replace, rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                }

                name = rule.Prefix + name + rule.Suffix;
                if (rule.NumberStart is int numberStart)
                {
                    var number = (long)numberStart + i;
                    var formatted = rule.NumberWidth > 0
                        ? number.ToString($"D{Math.Clamp(rule.NumberWidth, 1, 8)}")
                        : number.ToString();
                    name += " " + formatted;
                }

                name = rule.Uppercase ? name.ToUpperInvariant() : rule.Lowercase ? name.ToLowerInvariant() : name;

                if (!rule.IncludeExtension)
                {
                    name += extension;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    error = "The new name cannot be empty.";
                }
                else if (name is "." or ".." || name.Length > 255 || name.Any(character => character < 32 || InvalidCharacters.Contains(character)) || name.EndsWith('.') || name.EndsWith(' ') || IsDeviceName(name))
                {
                    error = $"'{name}' is not a valid file name.";
                }
            }

            var target = parent is null || error is not null ? source : Path.Combine(parent, name);
            if (error is null && !targets.Add(target))
            {
                error = $"Multiple items would be renamed to '{name}'.";
            }
            else if (error is null && pathExists?.Invoke(target) == true && !sourceSet.Contains(target))
            {
                error = $"The target '{name}' already exists.";
            }

            entries.Add(new BatchRenameEntry(source, target, error));
        }

        // Every participant in a collision must be marked, including the first row.
        var duplicates = entries.Where(e => e.Error is null || e.Error.StartsWith("Multiple items", StringComparison.Ordinal))
            .GroupBy(e => e.Target, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new BatchRenamePlan(entries.Select(e => duplicates.Contains(e.Target)
            ? e with { Error = $"Multiple items would be renamed to '{Path.GetFileName(e.Target)}'." } : e).ToArray());
    }

    private static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³'));
    }
}
