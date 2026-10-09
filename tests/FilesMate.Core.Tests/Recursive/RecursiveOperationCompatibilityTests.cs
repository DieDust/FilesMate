using FilesMate.Core.Navigation;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveOperationCompatibilityTests
{
    [Fact]
    public async Task Default_optional_verification_preserves_existing_api_and_never_invokes_capability_probe()
    {
        var probe = new CompatibilityProbe { Read = (_, _) => throw new InvalidOperationException("must not probe") };
        await using var session = CreateSession(Tree((Root, [Directory("parent")]),
            (Root + @"\parent", [File("child.txt")])), probe, required: false);
        await session.RunAsync().WaitAsync(TestTimeout);
        var snapshot = session.CurrentSnapshot;
        Assert.Empty(probe.Paths);
        Assert.Equal(RecursiveOperationCompatibility.NotRequired, snapshot.RootOperationCompatibility);
        Assert.Equal(RecursiveOperationCompatibility.NotRequired, snapshot.GetNode(Id(snapshot, "parent")).OperationCompatibility);
        Assert.True(Project(snapshot).EvaluateOperationSafety(Enumerable.Range(1, snapshot.Count)).IsSafe);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("false")]
    [InlineData("io")]
    [InlineData("unexpected")]
    public async Task Required_missing_false_or_throwing_probe_preserves_rows_but_never_grants_compatibility(string failure)
    {
        CompatibilityProbe? probe = failure == "missing" ? null : new()
        {
            Read = (_, _) => failure switch
            {
                "false" => false,
                "io" => throw new IOException("fixture unavailable"),
                _ => throw new InvalidOperationException("fixture unexpected failure")
            }
        };
        var enumerator = Tree((Root, [Directory("parent"), File("root.txt")]),
            (Root + @"\parent", [File("child.txt")]));
        await using var session = CreateSession(enumerator, probe);
        Assert.Equal(RecursiveOperationCompatibility.Pending, session.CurrentSnapshot.RootOperationCompatibility);
        await session.RunAsync().WaitAsync(TestTimeout);
        var snapshot = session.CurrentSnapshot;
        var projection = Project(snapshot);
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(new[] { Root, Root + @"\parent" }, enumerator.Requests.Select(r => r.Path));
        Assert.True(snapshot.RootListingComplete);
        Assert.Equal(RecursiveOperationCompatibility.Unverified, snapshot.RootOperationCompatibility);
        Assert.Equal(RecursiveOperationCompatibility.Unverified, snapshot.GetNode(Id(snapshot, "parent")).OperationCompatibility);
        var result = projection.EvaluateOperationSafety(Enumerable.Range(1, snapshot.Count));
        Assert.Equal(RecursiveOperationRisk.UnverifiedCompatibility, result.Risk);
        Assert.False(result.IsSafe);
        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(3, result.UnsafeCount);
        Assert.Equal(new[] { Root + @"\parent\child.txt", Root + @"\root.txt" },
            projection.ResolveOperationPaths([Id(snapshot, @"parent\child.txt"), Id(snapshot, "root.txt")]));
        if (probe is not null) Assert.Equal(new[] { Root, Root + @"\parent" }, probe.Paths);
    }

    [Fact]
    public async Task Unverified_root_compatibility_is_inherited_even_by_affirmatively_verified_branches()
    {
        var probe = new CompatibilityProbe { Read = (path, _) => path != Root };
        await using var session = CreateSession(Tree((Root, [Directory("parent")]),
            (Root + @"\parent", [File("child.txt")])), probe);
        await session.RunAsync().WaitAsync(TestTimeout);
        var snapshot = session.CurrentSnapshot;
        var projection = Project(snapshot);
        var parent = Id(snapshot, "parent");
        var child = Id(snapshot, @"parent\child.txt");
        Assert.Equal(RecursiveOperationCompatibility.Unverified, snapshot.RootOperationCompatibility);
        Assert.Equal(RecursiveOperationCompatibility.Compatible, snapshot.GetNode(parent).OperationCompatibility);
        Assert.Equal(RecursiveOperationCompatibility.NotRequired, snapshot.GetNode(child).OperationCompatibility);
        foreach (var id in new[] { parent, child })
        {
            Assert.Equal(RecursiveOperationRisk.UnverifiedCompatibility, projection.GetOperationRisk(id));
            Assert.False(projection.IsOperationSafe(id, RecursiveOperationScope.IdentityOnly));
        }
    }

    [Fact]
    public async Task Unverified_branch_blocks_covered_ancestor_and_descendant_but_not_safe_unrelated_branches()
    {
        var probe = new CompatibilityProbe { Read = (path, _) => path != Root + @"\parent\unverified" };
        await using var session = CreateSession(Tree((Root, [Directory("parent"), Directory("safe")]),
            (Root + @"\parent", [Directory("unverified"), File("sibling.txt")]),
            (Root + @"\parent\unverified", [File("child.txt")]),
            (Root + @"\safe", [File("child.txt")])), probe);
        await session.RunAsync().WaitAsync(TestTimeout);
        var snapshot = session.CurrentSnapshot;
        var projection = Project(snapshot);
        var parent = Id(snapshot, "parent");
        var unverified = Id(snapshot, @"parent\unverified");
        var child = Id(snapshot, @"parent\unverified\child.txt");
        Assert.Equal(6, snapshot.Count);
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        Assert.Equal(RecursiveOperationCompatibility.Compatible, snapshot.RootOperationCompatibility);
        Assert.Equal(RecursiveOperationCompatibility.Compatible, snapshot.GetNode(parent).OperationCompatibility);
        Assert.Equal(RecursiveOperationCompatibility.Unverified, snapshot.GetNode(unverified).OperationCompatibility);
        Assert.True(projection.IsOperationSafe(parent, RecursiveOperationScope.IdentityOnly));
        foreach (var id in new[] { parent, unverified, child })
            Assert.Equal(RecursiveOperationRisk.UnverifiedCompatibility, projection.GetOperationRisk(id));
        Assert.False(projection.IsOperationSafe(unverified, RecursiveOperationScope.IdentityOnly));
        Assert.False(projection.IsOperationSafe(child, RecursiveOperationScope.IdentityOnly));
        var safe = projection.EvaluateOperationSafety([Id(snapshot, @"parent\sibling.txt"),
            Id(snapshot, "safe"), Id(snapshot, @"safe\child.txt")]);
        Assert.True(safe.IsSafe);
        Assert.Equal(3, safe.SelectedCount);
        Assert.Equal(0, safe.UnsafeCount);
        Assert.Equal(RecursiveOperationRisk.None, safe.Risk);
    }

    [Fact]
    public async Task Affirmative_probe_is_captured_once_per_directory_and_safety_queries_do_not_probe_again()
    {
        var probe = new CompatibilityProbe { Read = (_, _) => true };
        await using var session = CreateSession(Tree((Root, [Directory("parent"), File("root.txt")]),
            (Root + @"\parent", [File("child.txt")])), probe);
        await session.RunAsync().WaitAsync(TestTimeout);
        var snapshot = session.CurrentSnapshot;
        Assert.Equal(new[] { Root, Root + @"\parent" }, probe.Paths);
        Assert.Equal(RecursiveOperationCompatibility.Compatible, snapshot.RootOperationCompatibility);
        Assert.Equal(RecursiveOperationCompatibility.Compatible, snapshot.GetNode(Id(snapshot, "parent")).OperationCompatibility);
        for (var i = 0; i < 3; i++)
        {
            var projection = Project(snapshot);
            Assert.True(projection.EvaluateOperationSafety(Enumerable.Range(1, snapshot.Count)).IsSafe);
            Assert.True(projection.IsOperationSafe(Id(snapshot, "parent"), RecursiveOperationScope.IdentityOnly));
        }
        Assert.Equal(new[] { Root, Root + @"\parent" }, probe.Paths);
    }

    [Fact]
    public async Task Compatibility_probe_cancellation_stops_before_provider_enumeration_and_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new CompatibilityProbe
        {
            Read = (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return true;
            }
        };
        var enumerator = Tree((Root, [File("must-not-enumerate.txt")]));
        await using var session = CreateSession(enumerator, probe);
        await session.RunAsync(cancellation.Token).WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Cancelled, session.State);
        Assert.Empty(session.CurrentSnapshot.Entries);
        Assert.Empty(enumerator.Requests);
        Assert.Equal(new[] { Root }, probe.Paths);
        Assert.Equal(RecursiveOperationCompatibility.Pending, session.CurrentSnapshot.RootOperationCompatibility);
    }

    private static RecursiveSession CreateSession(ScriptedEnumerator enumerator, CompatibilityProbe? probe, bool required = true) =>
        new(enumerator, PaneId.New(), 7, Root,
            new() { RequireOperationCompatibilityVerification = required }, new Probe(), probe);

    private sealed class CompatibilityProbe : IRecursiveOperationCompatibilityProbe
    {
        public required Func<string, CancellationToken, bool> Read { get; init; }
        public List<string> Paths { get; } = [];

        public bool IsLegacyOperationCompatible(string path, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            return Read(path, cancellationToken);
        }
    }
}
