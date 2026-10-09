using FilesMate.Search;

namespace FilesMate.Search.Tests;

public sealed class FeatureTourHistoryTests
{
    [Theory]
    [InlineData(true, false, FeatureTourOffer.FirstRun)]
    [InlineData(false, false, FeatureTourOffer.Release)]
    [InlineData(false, true, FeatureTourOffer.Release)]
    [InlineData(true, true, FeatureTourOffer.Release)]
    public void LegacyProfilesWithoutNewFieldsNeverBecomeFirstInstall(bool fresh, bool completed, FeatureTourOffer expected) =>
        Assert.Equal(expected, new FeatureTourHistory().Decide(fresh, completed, "release", ["tree"]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShowingThenSkippingPreventsAnotherOffer(bool first)
    {
        var shown = new FeatureTourHistory().Offered(first, "release", ["tree", "filters"]);
        Assert.Equal(FeatureTourOffer.None, shown.Decide(first, false, "release", ["tree", "filters"]));
        Assert.Empty(shown.CompletedReleases ?? []);
    }

    [Fact]
    public void PatchAndUpgradeJumpDeduplicationUsesFeatureIdentity()
    {
        var history = new FeatureTourHistory().Offered(false, "stable-feature-release", ["tree", "filters"]);
        Assert.Equal(FeatureTourOffer.None, history.Decide(false, true, "stable-feature-release", ["tree", "filters"]));
        Assert.Equal(FeatureTourOffer.None, history.Decide(false, true, "later-release", ["tree"]));
        Assert.Equal(FeatureTourOffer.Release, history.Decide(false, true, "later-release", ["tree", "new-feature"]));
    }

    [Fact]
    public void CompletionClearsDraftWithoutChangingOfferedIdentities()
    {
        var history = new FeatureTourHistory(Draft: new("guide", "search", "fingerprint", "{}"))
            .Offered(true, "release", ["tree"]).Completed("release");
        Assert.Null(history.Draft);
        Assert.Contains("release", history.CompletedReleases!);
        Assert.Contains("tree", history.OfferedFeatures!);
    }

    [Fact]
    public void FirstRunIncludesReleaseFeaturesSoGuidesNeverStack()
    {
        var history = new FeatureTourHistory().Offered(true, FeatureTourHistory.CurrentRelease, FeatureTourHistory.CurrentFeatures);
        Assert.Equal(FeatureTourOffer.None, history.Decide(false, false, FeatureTourHistory.CurrentRelease, FeatureTourHistory.CurrentFeatures));
    }

    [Theory]
    [InlineData("explorer-preferences.json")]
    [InlineData("features.json")]
    [InlineData("settings.json")]
    public void ExistingConfigurationCountsAsExistingUser(string name)
    {
        WithProfile(profile =>
        {
            File.WriteAllText(Path.Combine(profile, name), "{}");
            Assert.True(FeatureTourHistory.HasExistingUserData(profile));
        });
    }

    [Fact]
    public void IsolatedMarkersAndDiagnosticsDoNotCountAsUserConfiguration()
    {
        WithProfile(profile =>
        {
            File.WriteAllText(Path.Combine(profile, ".filesmate-isolated-profile"), "test");
            Directory.CreateDirectory(Path.Combine(profile, "temp"));
            Directory.CreateDirectory(Path.Combine(profile, "diagnostics"));
            Assert.False(FeatureTourHistory.HasExistingUserData(profile));
        });
    }

    [Fact]
    public void RuntimeIsolationReceiptsPreserveFirstRunUntilUserPreferencesExist()
    {
        WithProfile(profile =>
        {
            File.WriteAllText(Path.Combine(profile, "runtime-profile-FilesMate.App.json"), "{\"PersistentIntegration\":false}");
            File.WriteAllText(Path.Combine(profile, "runtime-profile-FilesMate.SearchHost.json"), "{\"PersistentIntegration\":false}");
            var fresh = !FeatureTourHistory.HasExistingUserData(profile);
            Assert.Equal(FeatureTourOffer.FirstRun, new FeatureTourHistory().Decide(fresh, false, "release", ["tree"]));
            File.WriteAllText(Path.Combine(profile, "explorer.json"), "{\"folderOpeningMode\":\"SingleClick\"}");
            fresh = !FeatureTourHistory.HasExistingUserData(profile);
            Assert.Equal(FeatureTourOffer.Release, new FeatureTourHistory().Decide(fresh, false, "release", ["tree"]));
        });
    }

    [Fact]
    public void OnlyExplicitIsolationTreatsWindowPlacementAsHarnessMetadata()
    {
        WithProfile(profile =>
        {
            File.WriteAllText(Path.Combine(profile, "window.json"), "{\"x\":-2520,\"y\":40}");
            Assert.True(FeatureTourHistory.HasExistingUserData(profile));
            Assert.False(FeatureTourHistory.HasExistingUserData(profile, isolatedPlacementMetadata: true));
            File.WriteAllText(Path.Combine(profile, "explorer.json"), "{\"folderOpeningMode\":\"SingleClick\"}");
            Assert.True(FeatureTourHistory.HasExistingUserData(profile, isolatedPlacementMetadata: true));
        });
    }

    [Fact]
    public void UnrecognizedRuntimeNamedConfigurationStillCountsAsExistingUser()
    {
        WithProfile(profile =>
        {
            File.WriteAllText(Path.Combine(profile, "runtime-profile-preferences.json"), "{\"custom\":true}");
            Assert.True(FeatureTourHistory.HasExistingUserData(profile));
        });
    }

    [Fact]
    public void DraftAndOfferedStateRoundTripWithoutChangingExplorerPreferences()
    {
        WithProfile(profile =>
        {
            var settings = Path.Combine(profile, "explorer-preferences.json");
            File.WriteAllText(settings, "original bytes");
            var history = new FeatureTourHistory(Draft: new("guide", "search", "same-preferences", "{\"Hotkey\":\"Ctrl+Alt+Space\"}"))
                .Offered(false, "release", ["tree"]);
            history.Save(profile);
            var loaded = FeatureTourHistory.Load(profile);
            Assert.Equal(history.Draft, loaded.Draft);
            Assert.Equal(history.OfferedFeatures, loaded.OfferedFeatures);
            Assert.Equal("original bytes", File.ReadAllText(settings));
            Assert.Single(Directory.GetFiles(profile, "feature-tour*"));
        });
    }

    [Fact]
    public void CorruptHistoryLeavesLegacyUserClassificationIntact()
    {
        WithProfile(profile =>
        {
            File.WriteAllText(FeatureTourHistory.FilePath(profile), "{bad");
            Assert.True(FeatureTourHistory.HasExistingUserData(profile));
            Assert.Equal(FeatureTourOffer.Release, FeatureTourHistory.Load(profile).Decide(false, false, "release", ["tree"]));
        });
    }

    private static void WithProfile(Action<string> test)
    {
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-Tour-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        try { test(profile); }
        finally { Directory.Delete(profile, recursive: true); }
    }
}
