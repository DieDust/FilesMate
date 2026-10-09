using System.Text.Json;

namespace FilesMate.Search;

public enum FeatureTourOffer { None, FirstRun, Release }

public sealed record FeatureTourDraft(string Catalog, string Page, string PreferencesFingerprint, string ChoicesJson);

/// <summary>Feature releases have stable IDs independent of patch-build numbers.</summary>
public sealed record FeatureTourHistory(
    bool FirstRunOffered = false,
    string[]? OfferedReleases = null,
    string[]? OfferedFeatures = null,
    string[]? CompletedReleases = null,
    FeatureTourDraft? Draft = null)
{
    public const string CurrentRelease = "recursive-browsing-2026-10-v1";
    public static readonly string[] CurrentFeatures = ["recursive-tree-v1", "compact-filters-v1", "paged-guide-v1"];
    public static string FilePath(string profile) => Path.Combine(profile, "feature-tour.json");

    public FeatureTourOffer Decide(bool profileWasNew, bool legacyCompleted, string release, IEnumerable<string> features)
    {
        if (profileWasNew && !legacyCompleted && !FirstRunOffered) return FeatureTourOffer.FirstRun;
        if ((OfferedReleases ?? []).Contains(release, StringComparer.Ordinal)) return FeatureTourOffer.None;
        return features.Except(OfferedFeatures ?? [], StringComparer.Ordinal).Any()
            ? FeatureTourOffer.Release : FeatureTourOffer.None;
    }

    public FeatureTourHistory Offered(bool firstRun, string release, IEnumerable<string> features) => this with
    {
        FirstRunOffered = FirstRunOffered || firstRun,
        OfferedReleases = Merge(OfferedReleases, [release]),
        OfferedFeatures = Merge(OfferedFeatures, features),
    };

    public FeatureTourHistory Completed(string release) => this with
    {
        CompletedReleases = Merge(CompletedReleases, [release]), Draft = null,
    };

    private static string[] Merge(string[]? previous, IEnumerable<string> incoming) =>
        (previous ?? []).Concat(incoming).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).TakeLast(64).ToArray();

    // Inspect before App initialization writes settings. A missing new tour field
    // never turns an existing profile into a new installation.
    public static bool HasExistingUserData(string profile, bool isolatedPlacementMetadata = false)
    {
        try
        {
            return Directory.Exists(profile) && Directory.EnumerateFileSystemEntries(profile).Any(path =>
            {
                var name = Path.GetFileName(path);
                return name is not ".filesmate-isolated-profile" and not "temp" and not "diagnostics"
                    and not "runtime-profile-FilesMate.App.json" and not "runtime-profile-FilesMate.SearchHost.json"
                    && !(isolatedPlacementMetadata && name == "window.json")
                    && !name.StartsWith("profile-check-", StringComparison.Ordinal);
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
    }

    public static FeatureTourHistory Load(string profile)
    {
        try { return JsonSerializer.Deserialize<FeatureTourHistory>(File.ReadAllText(FilePath(profile))) ?? new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string profile)
    {
        Directory.CreateDirectory(profile);
        var target = FilePath(profile);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
