using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace FilesMate.Core.Entries;

[Flags]
public enum EntryTypeCategory { None = 0, Documents = 1, Images = 2, Audio = 4, Video = 8, Archives = 16, Applications = 32, Directories = 64 }

/// <summary>Immutable predicates over captured metadata. No filesystem or directory-size requests.</summary>
public sealed record EntryMetadataFilter
{
    public static EntryMetadataFilter None { get; } = new();
    public EntryTypeCategory Categories { get; init; }
    public ImmutableArray<string> Extensions { get; init; } = [];
    public bool NoExtension { get; init; }
    public DateOnly? ModifiedStartDate { get; init; }
    public DateOnly? ModifiedEndDate { get; init; }
    public long? ModifiedFromUtcTicks { get; init; }
    public long? ModifiedUntilUtcTicks { get; init; }
    public ulong? MinimumBytes { get; init; }
    public ulong? MaximumBytes { get; init; }
    public bool HasTypes => Categories != EntryTypeCategory.None || !Extensions.IsDefaultOrEmpty || NoExtension;
    public bool HasDate => ModifiedFromUtcTicks is not null || ModifiedUntilUtcTicks is not null;
    public bool HasSize => MinimumBytes is not null || MaximumBytes is not null;
    public bool IsActive => HasTypes || HasDate || HasSize;

    private static readonly (EntryTypeCategory Category, FrozenSet<string> Extensions)[] Groups =
    [
        (EntryTypeCategory.Documents, Set(".pdf .doc .docx .odt .xls .xlsx .csv .ppt .pptx .txt .md .rtf .epub")),
        (EntryTypeCategory.Images, Set(".jpg .jpeg .png .gif .webp .svg .bmp .tif .tiff .ico .heic .avif")),
        (EntryTypeCategory.Audio, Set(".mp3 .wav .flac .aac .ogg .m4a .wma .opus")),
        (EntryTypeCategory.Video, Set(".mp4 .mkv .mov .avi .webm .wmv .m4v .mpeg .mpg")),
        (EntryTypeCategory.Archives, Set(".zip .7z .rar .tar .gz .bz2 .xz .zst")),
        (EntryTypeCategory.Applications, Set(".exe .msi .msix .com .bat .cmd .ps1"))
    ];
    private static FrozenSet<string> Set(string text) => text.Split(' ').ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public bool Matches(in FileEntryCore entry)
    {
        if (HasTypes && !MatchesType(entry)) return false;
        if (ModifiedFromUtcTicks is { } from && entry.ModifiedUtcTicks < from) return false;
        if (ModifiedUntilUtcTicks is { } until && entry.ModifiedUtcTicks >= until) return false;
        // Directory entry sizes do not describe subtree bytes. Never calculate
        // them as a side effect of applying a filter.
        if (HasSize && (entry.Kind != EntryKind.File
            || MinimumBytes is { } min && entry.Size < min || MaximumBytes is { } max && entry.Size > max)) return false;
        return true;
    }

    private bool MatchesType(in FileEntryCore entry)
    {
        if (entry.Kind == EntryKind.Directory) return Categories.HasFlag(EntryTypeCategory.Directories);
        if (entry.Kind != EntryKind.File) return false;
        var extension = Path.GetExtension(entry.Name);
        if (extension.Length == 0 && NoExtension) return true;
        if (!Extensions.IsDefaultOrEmpty && Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return true;
        return Groups.Any(group => (Categories & group.Category) != 0 && group.Extensions.Contains(extension));
    }

    public static bool TryNormalizeExtensions(string? text, out ImmutableArray<string> extensions)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in (text ?? "").Split([';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = token.StartsWith("*.", StringComparison.Ordinal) ? token[1..] : token.StartsWith('.') ? token : "." + token;
            // One extension, not a path, arbitrary wildcard, or compound suffix.
            if (value.Length is < 2 or > 32 || !value.AsSpan(1).ToString().All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            { extensions = []; return false; }
            result.Add(value.ToLowerInvariant());
            if (result.Count > 64) { extensions = []; return false; }
        }
        extensions = result.ToImmutableArray();
        return true;
    }

    public static bool TryDateRange(DateOnly? first, DateOnly? last, TimeZoneInfo zone,
        out long? fromUtcTicks, out long? untilUtcTicks)
    {
        fromUtcTicks = untilUtcTicks = null;
        if (first is null && last is null || first is { } a && last is { } b && a > b) return false;
        try
        {
            if (first is { } start) fromUtcTicks = Boundary(start, zone);
            if (last is { } end) untilUtcTicks = Boundary(end.AddDays(1), zone);
            return fromUtcTicks is null || untilUtcTicks is null || fromUtcTicks < untilUtcTicks;
        }
        catch (ArgumentException) { fromUtcTicks = untilUtcTicks = null; return false; }
    }

    private static long Boundary(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones change offsets at midnight, including skipped calendar dates.
        for (var minute = 0; zone.IsInvalidTime(local); minute++)
        {
            if (minute >= 1440) throw new ArgumentException("Invalid calendar boundary");
            local = local.AddMinutes(1);
        }
        if (zone.IsAmbiguousTime(local)) return (local - zone.GetAmbiguousTimeOffsets(local).Max()).Ticks;
        return TimeZoneInfo.ConvertTimeToUtc(local, zone).Ticks;
    }

    public static bool TryByteRange(string? minimum, string? maximum, ulong multiplier, CultureInfo culture,
        out ulong? min, out ulong? max)
    {
        min = max = null;
        if (multiplier == 0 || string.IsNullOrWhiteSpace(minimum) && string.IsNullOrWhiteSpace(maximum)) return false;
        if (!Parse(minimum, multiplier, culture, true, out min) || !Parse(maximum, multiplier, culture, false, out max)
            || min is { } a && max is { } b && a > b) { min = max = null; return false; }
        return true;
    }

    private static bool Parse(string? text, ulong multiplier, CultureInfo culture, bool minimum, out ulong? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text, NumberStyles.Number, culture, out var number) || number < 0) return false;
        try
        {
            var exact = number * multiplier;
            // Integer captured bytes: round the lower bound up, upper bound down.
            bytes = checked((ulong)(minimum ? decimal.Ceiling(exact) : decimal.Floor(exact)));
            return true;
        }
        catch (OverflowException) { return false; }
    }
}
