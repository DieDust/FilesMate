using System.Globalization;
using System.Text.RegularExpressions;
using FilesMate.Core.Archives;

namespace FilesMate.Platform.Windows.Archives;

public sealed record ArchiveSet(string PrimaryPath, IReadOnlyList<string> Volumes, string Name)
{
    public bool HasDecoratedNames => Volumes.Any(path => !Path.GetFileName(path).Equals(
        ArchiveFileName.CanonicalName(path), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Resolve selected volumes without renaming, joining or changing source files.</summary>
public static partial class ArchiveSetResolver
{
    [GeneratedRegex(@"^(.*)\.part([0-9]+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartRar();
    [GeneratedRegex(@"^(.*)\.([0-9]{3,})$", RegexOptions.CultureInvariant)]
    private static partial Regex Numeric();
    [GeneratedRegex(@"^(.*)\.([rz])([0-9]{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Legacy();

    public static IReadOnlyList<ArchiveSet> Resolve(IReadOnlyList<string> selected, CancellationToken token = default)
    {
        var result = new List<ArchiveSet>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in selected)
        {
            token.ThrowIfCancellationRequested();
            var full = ArchivePathGuard.LocalPath(path);
            if (!File.Exists(full) || !ArchiveFileName.IsArchive(full))
                throw ArchivePathGuard.Error(ArchiveErrorCode.UnsupportedEntry);
            var directory = Path.GetDirectoryName(full)!;
            var info = Describe(ArchiveFileName.CanonicalName(full));
            var key = Path.Combine(directory, info.Key);
            if (info.Kind == "single")
            {
                if (!seen.Add(full)) continue;
                result.Add(new(full, [full], Stem(info.Key)));
                continue;
            }
            if (!directories.TryGetValue(directory, out var siblings))
                directories.Add(directory, siblings = Directory.GetFiles(directory));
            if (info.Kind is "rar" or "zip" && info.Number == 0 && !siblings.Any(sibling =>
                {
                    var other = Describe(ArchiveFileName.CanonicalName(sibling));
                    return other.Kind == info.Kind && other.Number > 0 && other.Key.Equals(info.Key, StringComparison.OrdinalIgnoreCase);
                }))
            {
                // A renamed ZIP and its original can both exist as independent archives.
                // Canonical aliases are ambiguous only when resolving a volume set.
                if (seen.Add(full)) result.Add(new(full, [full], Stem(info.Key)));
                continue;
            }
            if (!seen.Add("volumes:" + info.Kind + ":" + key)) continue;
            var members = new SortedDictionary<int, string>();
            foreach (var sibling in siblings)
            {
                token.ThrowIfCancellationRequested();
                var other = Describe(ArchiveFileName.CanonicalName(sibling));
                if (other.Kind != info.Kind || !other.Key.Equals(info.Key, StringComparison.OrdinalIgnoreCase)) continue;
                if (!members.TryAdd(other.Number, sibling)) throw ArchivePathGuard.Error(ArchiveErrorCode.InvalidArchive);
            }
            var start = info.Kind is "rar" or "zip" ? 0 : 1;
            if (!members.ContainsKey(start)) throw ArchivePathGuard.Error(ArchiveErrorCode.MissingVolume);
            var expected = start;
            foreach (var number in members.Keys)
                if (number != expected++) throw ArchivePathGuard.Error(ArchiveErrorCode.MissingVolume);
            // SharpCompress expects the central-directory (.zip) stream first, then .z01, .z02...
            var volumes = members.Values.ToArray();
            result.Add(new(members[start], volumes, Stem(info.Key)));
        }
        return result;
    }

    private static (string Kind, string Key, int Number) Describe(string name)
    {
        var part = PartRar().Match(name);
        if (part.Success) return ("part", part.Groups[1].Value + ".rar", Number(part.Groups[2].Value));
        var numeric = Numeric().Match(name);
        if (numeric.Success) return ("numeric", numeric.Groups[1].Value, Number(numeric.Groups[2].Value));
        var legacy = Legacy().Match(name);
        if (legacy.Success)
        {
            var rar = legacy.Groups[2].Value.Equals("r", StringComparison.OrdinalIgnoreCase);
            return (rar ? "rar" : "zip", legacy.Groups[1].Value + (rar ? ".rar" : ".zip"),
                Number(legacy.Groups[3].Value) + (rar ? 1 : 0));
        }
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return (extension is ".rar" or ".zip" ? extension[1..] : "single", name, 0);
    }

    private static int Number(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n < 100_000 ? n : throw ArchivePathGuard.Error(ArchiveErrorCode.TooManyEntries);

    private static string Stem(string name)
    {
        var result = ArchiveFileName.IsArchive(name) ? Path.GetFileNameWithoutExtension(name) : name;
        if (result.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) result = result[..^4];
        ArchivePathGuard.ValidateSegment(result);
        return result;
    }
}
