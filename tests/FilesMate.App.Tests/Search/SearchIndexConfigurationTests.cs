using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Search;

namespace FilesMate.App.Tests.Search;

public sealed class SearchIndexConfigurationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("FilesMate-IndexSettings-").FullName;
    private string SettingsPath => Path.Combine(_root, "search-index.json");
    private SearchIndexSettingsService Store => new(SettingsPath);
    private SearchIndexSettings Initial => SearchIndexSettings.Sanitize([_root], ["excluded"], false, databaseDirectory: _root);

    [Fact]
    public async Task Stale_page_save_preserves_relocated_directory_host_ranking_and_unknown_fields()
    {
        var stale = Initial;
        await Store.SaveAsync(stale);
        SearchIndexConfigurationFile.Update(SettingsPath, document => document["futureOption"] = 42);
        var relocated = Path.Combine(_root, "new-location");
        await Store.UpdateDatabaseDirectoryAsync(relocated);
        SearchRankingConfiguration.Save([SearchHitKind.Folder], _root);

        await Store.SaveAsync(stale with { MaxDepth = 9 });

        Assert.Equal(relocated, Store.Load().DatabaseDirectory);
        Assert.Equal(SearchHitKind.Folder, Store.Load().RankOrder[0]);
        Assert.Equal(9, Store.Load().MaxDepth);
        Assert.Equal(42, SearchIndexConfigurationFile.Read(SettingsPath)["futureOption"]!.GetValue<int>());
    }

    [Fact]
    public async Task Concurrent_relocation_ranking_and_stale_page_saves_keep_each_owners_fields()
    {
        var stale = Initial;
        await Store.SaveAsync(stale);
        var relocated = Path.Combine(_root, "relocated");
        using var start = new ManualResetEventSlim();
        var page = Task.Run(async () =>
        {
            start.Wait();
            for (var i = 0; i < 20; i++) await Store.SaveAsync(stale with { MaxDepth = 11 });
        });
        var ranking = Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 20; i++) SearchRankingConfiguration.Save([SearchHitKind.Image], _root);
        });
        var relocation = Task.Run(async () =>
        {
            start.Wait();
            for (var i = 0; i < 20; i++) await Store.UpdateDatabaseDirectoryAsync(relocated);
        });
        start.Set();
        await Task.WhenAll(page, ranking, relocation).WaitAsync(TimeSpan.FromSeconds(20));

        var settings = Store.Load();
        Assert.Equal(relocated, settings.DatabaseDirectory);
        Assert.Equal(SearchHitKind.Image, settings.RankOrder[0]);
        Assert.Equal(11, settings.MaxDepth);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task External_process_lock_blocks_publish_until_released_and_cancellation_keeps_old_settings()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Store.SaveAsync(Initial);
        var original = await File.ReadAllBytesAsync(SettingsPath);
        var ready = Path.Combine(_root, "ready");
        var release = Path.Combine(_root, "release");
        static string Encoded(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        var script = """
            $lockPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__LOCK__'))
            $readyPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__READY__'))
            $releasePath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__RELEASE__'))
            $gate = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try {
                [IO.File]::WriteAllText($readyPath, 'ready')
                $deadline = [DateTime]::UtcNow.AddSeconds(15)
                while (-not [IO.File]::Exists($releasePath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 20 }
            } finally { $gate.Dispose() }
            """.Replace("__LOCK__", Encoded(SettingsPath + ".lock"), StringComparison.Ordinal)
            .Replace("__READY__", Encoded(ready), StringComparison.Ordinal)
            .Replace("__RELEASE__", Encoded(release), StringComparison.Ordinal);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var child = Process.Start(start)!;
        try
        {
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(ready)) await Task.Delay(20, readyTimeout.Token);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.UpdateDatabaseDirectoryAsync(Path.Combine(_root, "cancelled"), cancellation.Token));
            Assert.Equal(original, await File.ReadAllBytesAsync(SettingsPath));

            var relocated = Path.Combine(_root, "published");
            var update = Store.UpdateDatabaseDirectoryAsync(relocated);
            await Task.Delay(100);
            Assert.False(update.IsCompleted);
            await File.WriteAllTextAsync(release, "release");
            await update.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(relocated, Store.Load().DatabaseDirectory);
        }
        finally
        {
            await File.WriteAllTextAsync(release, "release");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{\"databaseDirectory\":123}")]
    [InlineData("{\"databaseDirectory\":[]}")]
    [InlineData("{\"databaseDirectory\":{}}")]
    [InlineData("{\"databaseDirectory\":null}")]
    [InlineData("{\"databaseDirectory\":\"relative-folder\"}")]
    [InlineData("{\"databaseDirectory\":\"\\u0000\"}")]
    [InlineData("{broken")]
    [InlineData("{\"databaseDirectory\":null,\"databaseDirectory\":null}")]
    public void Database_resolver_rejects_wrong_json_types_and_invalid_or_relative_paths(string json)
    {
        File.WriteAllText(SettingsPath, json);
        Assert.Equal(Path.Combine(_root, "search-index.db"), GlobalSearchConfiguration.ResolveDatabase(_root));
    }

    [Fact]
    public void Database_resolver_honors_case_insensitive_absolute_location()
    {
        var location = Path.Combine(_root, "new-location");
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { DatabaseDirectory = location }));
        Assert.Equal(Path.Combine(location, "search-index.db"), GlobalSearchConfiguration.ResolveDatabase(_root));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("{\"databaseDirectory\":null,\"databaseDirectory\":null}")]
    public async Task Save_does_not_overwrite_malformed_existing_configuration(string original)
    {
        await File.WriteAllTextAsync(SettingsPath, original);
        await Assert.ThrowsAnyAsync<JsonException>(() => Store.SaveAsync(Initial));
        await Assert.ThrowsAnyAsync<JsonException>(() => Store.UpdateDatabaseDirectoryAsync(Path.Combine(_root, "unused")));
        Assert.ThrowsAny<JsonException>(() => SearchRankingConfiguration.Save([SearchHitKind.Image], _root));
        Assert.Equal(original, await File.ReadAllTextAsync(SettingsPath));
    }

    [Fact]
    public async Task Saving_page_or_location_preserves_configuration_with_invalid_known_field_types()
    {
        const string original = "{\"databaseDirectory\":[],\"futureOption\":42}";
        await File.WriteAllTextAsync(SettingsPath, original);
        await Assert.ThrowsAsync<JsonException>(() => Store.SaveAsync(Initial));
        await Assert.ThrowsAsync<JsonException>(() => Store.UpdateDatabaseDirectoryAsync(Path.Combine(_root, "unused")));
        Assert.Equal(original, await File.ReadAllTextAsync(SettingsPath));
    }

    [Fact]
    public async Task Explicit_rank_edit_updates_rank_without_reverting_the_published_directory()
    {
        var stale = Initial;
        await Store.SaveAsync(stale);
        var location = Path.Combine(_root, "moved");
        await Store.UpdateDatabaseDirectoryAsync(location);
        await Store.SaveAsync(stale with { RankOrder = [SearchHitKind.Folder] }, updateRankOrder: true);
        Assert.Equal(location, Store.Load().DatabaseDirectory);
        Assert.Equal(SearchHitKind.Folder, Store.Load().RankOrder[0]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
