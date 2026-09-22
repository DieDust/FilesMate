using System.ComponentModel;
using FilesMate.App.Commands;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;

namespace FilesMate.App.Tests.Commands;

public sealed class CompactMateRoutingTests
{
    [Fact]
    public void Missing_provider_returns_false_without_starting_any_process()
    {
        var started = 0;
        Assert.False(CompactMateSession.TryLaunch(CompactMateVerb.CompressZip, ["input.txt"],
            new MemoryUserRegistry(), _ => null, _ => started++));
        Assert.Equal(0, started);
    }

    [Theory]
    [InlineData(CompactMateVerb.CompressZip)]
    [InlineData(CompactMateVerb.Compress7z)]
    [InlineData(CompactMateVerb.ExtractHere)]
    [InlineData(CompactMateVerb.SmartExtract)]
    [InlineData(CompactMateVerb.Open)]
    public void Installed_provider_keeps_the_existing_launch_contract(CompactMateVerb verb)
    {
        const string executable = @"C:\Tools\CompactMate.exe";
        const string source = @"C:\Input folder\payload.zip";
        CompactMateLaunch? started = null;
        Assert.True(CompactMateSession.TryLaunch(verb, [source], new MemoryUserRegistry(),
            _ => executable, launch => started = launch));
        Assert.Equal(CompactMateHost.Create(executable, verb, [source]), started);
    }

    [Fact]
    public void Registered_archive_handler_is_preferred_over_other_candidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-provider-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archiveHandler = Path.Combine(root, "handler.exe");
        var appPath = Path.Combine(root, "app-path.exe");
        try
        {
            File.WriteAllBytes(archiveHandler, []);
            File.WriteAllBytes(appPath, []);
            var registry = new MemoryUserRegistry();
            registry.SetDefaultValue(CompactMateHost.ArchiveOpenCommandKey, $"\"{archiveHandler}\" \"%1\"");
            registry.SetDefaultValue(CompactMateHost.AppPathKey, appPath);
            CompactMateLaunch? started = null;
            Assert.True(CompactMateSession.TryLaunch(CompactMateVerb.ExtractHere, ["input.zip"], registry,
                source => CompactMateHost.TryFind(source, [], out var executable, includeNearby: false) ? executable : null,
                launch => started = launch));
            Assert.Equal(archiveHandler, started?.FileName);
        }
        finally
        {
            File.Delete(archiveHandler);
            File.Delete(appPath);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Found_provider_start_failure_is_not_reported_as_missing_or_retried()
    {
        var expected = new Win32Exception(5);
        var attempts = 0;
        var usedBuiltIn = false;
        var actual = Assert.Throws<Win32Exception>(() =>
        {
            if (!CompactMateSession.TryLaunch(CompactMateVerb.CompressZip, ["input.txt"], new MemoryUserRegistry(),
                _ => @"C:\Tools\CompactMate.exe", _ => { attempts++; throw expected; }))
                usedBuiltIn = true;
        });
        Assert.Same(expected, actual);
        Assert.Equal(1, attempts);
        Assert.False(usedBuiltIn);
    }

    [Fact]
    public void Discovery_failure_is_not_reported_as_missing()
    {
        Assert.Throws<UnauthorizedAccessException>(() => CompactMateSession.TryLaunch(
            CompactMateVerb.ExtractHere, ["input.zip"], new MemoryUserRegistry(),
            _ => throw new UnauthorizedAccessException(), _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void Empty_selection_fails_before_discovery_or_launch()
    {
        Assert.Throws<IOException>(() => CompactMateSession.TryLaunch(CompactMateVerb.CompressZip, [],
            new MemoryUserRegistry(), _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()));
    }

    [Theory]
    [InlineData(AppCommandId.Compress7z, true)]
    [InlineData(AppCommandId.OpenInCompactMate, true)]
    [InlineData(AppCommandId.CompressZip, false)]
    [InlineData(AppCommandId.CompressNew, false)]
    [InlineData(AppCommandId.ExtractHere, false)]
    [InlineData(AppCommandId.ExtractToFolder, false)]
    [InlineData(AppCommandId.ExtractToOther, false)]
    [InlineData(AppCommandId.SmartExtract, false)]
    public void Basic_zip_and_extract_commands_do_not_require_external_provider(AppCommandId id, bool required) =>
        Assert.Equal(required, CompactMateSession.RequiresExternalProvider(id));

    [Fact]
    public async Task Menu_probes_run_asynchronously_and_share_inflight_work()
    {
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var probe = new CompactMateAvailability(() =>
        {
            Interlocked.Increment(ref calls);
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return true;
        });
        var first = probe.GetAsync();
        try
        {
            Assert.False(first.IsCompleted);
            for (var i = 0; i < 30; i++) Assert.Same(first, probe.GetAsync(forceRefresh: true));
        }
        finally { release.Set(); }
        Assert.True(await first);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cached_absence_refreshes_after_install_and_can_be_forced()
    {
        var clock = new ManualClock();
        var installed = false;
        var calls = 0;
        var probe = new CompactMateAvailability(() => { calls++; return installed; }, clock);
        Assert.False(await probe.GetAsync());
        installed = true;
        Assert.False(await probe.GetAsync());
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(await probe.GetAsync());
        installed = false;
        Assert.False(await probe.GetAsync(forceRefresh: true));
        Assert.Equal(3, calls);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
