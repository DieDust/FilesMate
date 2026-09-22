#if FILESMATE_UI_TEST
using System.Security.Cryptography;
using System.Text.Json;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Search;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunIndexStorageSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        Microsoft.UI.Xaml.Window? observerWindow = null;
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            var profile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-profile")) + Path.DirectorySeparatorChar;
            var store = App.SearchIndexSettingsStore ?? throw new IOException("Search settings unavailable.");
            var index = App.SearchIndex ?? throw new IOException("Search index unavailable.");
            // The launcher must seed its isolated profile before starting the test process.
            // A missing fixture must never turn this smoke into a migration of the user's index.
            if (!Path.GetFullPath(store.FilePath).StartsWith(profile, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(index.FilePath).StartsWith(profile, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(store.Load().ResolveDatabasePath()).StartsWith(profile, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Refusing index smoke outside the private UI-test profile.");

            var root = Directory.CreateDirectory(Path.Combine(profile, "index-smoke-" + Guid.NewGuid().ToString("N"))).FullName;
            var fixtures = Directory.CreateDirectory(Path.Combine(root, "fixtures")).FullName;
            var needle = Path.Combine(fixtures, "smoke-needle.txt");
            File.WriteAllText(needle, "isolated search fixture");
            File.WriteAllText(Path.Combine(fixtures, "another.txt"), "second fixture");
            var settings = new SearchIndexSettings([fixtures], [], false, 1, Path.GetDirectoryName(index.FilePath)!, false, SearchHitKinds.DefaultOrder);
            await store.SaveAsync(settings, updateDatabaseDirectory: true);
            await index.RebuildAsync(settings);
            if (index.Stats.CompletedUtc is null || index.Stats.Files != 2) throw new IOException("Fixture index did not complete.");
            result["FixtureFiles"] = index.Stats.Files;

            OpenSettings("search");
            SearchSettingsPage? page = null;
            for (var attempt = 0; attempt < 150; attempt++)
            {
                if (_settingsPage is not null && FindDescendant<SearchSettingsPage>(_settingsPage, item => item.IsLoaded) is { } loaded)
                { page = loaded; break; }
                await Task.Delay(100);
            }
            if (page is null) throw new IOException("Search settings page did not load.");
            var size = page.FindName("StatSizeValue") as TextBlock ?? throw new IOException("Index size control missing.");
            var completed = page.FindName("StatCompletedValue") as TextBlock ?? throw new IOException("Completion time control missing.");
            await WaitForUsageAsync(page, size, index.FilePath);
            var expectedCompleted = index.Stats.CompletedUtc.Value.ToLocalTime().ToString("g");
            if (completed.Text != expectedCompleted) throw new IOException("Saved completion time was not shown.");
            result["IndexSizeBefore"] = size.Text;
            result["LastCompleted"] = completed.Text;

            var observer = new SearchSettingsPage();
            observerWindow = new Microsoft.UI.Xaml.Window { Content = observer };
            observerWindow.AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            observerWindow.Activate();
            for (var attempt = 0; attempt < 100 && !observer.IsLoaded; attempt++) await Task.Delay(50);
            if (!observer.IsLoaded) throw new IOException("Second search settings window did not load.");

            var targetDirectory = Path.Combine(root, "moved-index");
            await page.ChangeLocationToAsync(targetDirectory);
            var movedPath = Path.Combine(targetDirectory, SearchIndexSettings.DatabaseFileName);
            RequireLocation(movedPath, store);
            await RequireNeedleAsync(movedPath, needle);
            await WaitForUsageAsync(page, size, movedPath);
            result["IndexSizeAfter"] = size.Text;
            result["MigrationQuery"] = true;
            result["PublishedPath"] = movedPath;
            await observer.RefreshUsageAsync();
            var observedLocation = observer.FindName("IndexLocationCard") as Controls.Settings.SettingCard;
            if (observedLocation?.Description != movedPath)
                throw new IOException("The second window kept the old index location.");
            result["OtherWindowTracksRelocation"] = true;

            var foreignDirectory = Directory.CreateDirectory(Path.Combine(root, "foreign-index")).FullName;
            var foreign = Path.Combine(foreignDirectory, SearchIndexSettings.DatabaseFileName);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = foreign, Pooling = false }.ToString()))
            {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE unrelated(value TEXT); INSERT INTO unrelated VALUES ('keep this database');";
                command.ExecuteNonQuery();
            }
            var foreignHash = SHA256.HashData(File.ReadAllBytes(foreign));
            var settingsBefore = File.ReadAllBytes(store.FilePath);
            var sourceBefore = SHA256.HashData(File.ReadAllBytes(movedPath));
            var refused = false;
            try { await page.ChangeLocationToAsync(foreignDirectory); }
            catch (IOException) { refused = true; }
            if (!refused) throw new IOException("An existing unrelated database was accepted as a migration destination.");
            RequireLocation(movedPath, store);
            if (!foreignHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(foreign)))
                || !settingsBefore.AsSpan().SequenceEqual(File.ReadAllBytes(store.FilePath))
                || !sourceBefore.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(movedPath))))
                throw new IOException("Rejected migration changed the source, configuration or foreign database.");
            await RequireNeedleAsync(movedPath, needle);
            await WaitForUsageAsync(page, size, movedPath);
            result["ForeignDatabasePreserved"] = true;
            result["FailureKeepsSearchWorking"] = true;
            var malformedTarget = Path.Combine(root, "malformed-settings-target");
            File.WriteAllText(store.FilePath, "{ invalid configuration");
            var invalidSettings = File.ReadAllBytes(store.FilePath);
            try
            {
                var failed = false;
                try { await page.ChangeLocationToAsync(malformedTarget); }
                catch (JsonException) { failed = true; }
                if (!failed || App.SearchIndex?.FilePath != movedPath
                    || !invalidSettings.AsSpan().SequenceEqual(File.ReadAllBytes(store.FilePath))
                    || File.Exists(Path.Combine(malformedTarget, SearchIndexSettings.DatabaseFileName)))
                    throw new IOException("Malformed settings did not preserve the active custom index.");
                await RequireNeedleAsync(movedPath, needle);
                result["MalformedSettingsKeepsActiveCustomIndex"] = true;
            }
            finally { File.WriteAllBytes(store.FilePath, settingsBefore); }
            result["Passed"] = true;
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        observerWindow?.Close();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "index-storage-smoke.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Close();

        static void RequireLocation(string expected, SearchIndexSettingsService store)
        {
            if (!string.Equals(App.SearchIndex?.FilePath, expected, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(store.Load().ResolveDatabasePath(), expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Live index and saved index location disagree.");
        }
        static async Task RequireNeedleAsync(string database, string expectedFile)
        {
            var hits = await Task.Run(() => NameIndexReader.Search(database, "smoke-needle", null, 10));
            if (!hits.Any(hit => string.Equals(hit.Path, expectedFile, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Migrated index lost its search result.");
        }
        static async Task WaitForUsageAsync(SearchSettingsPage page, TextBlock size, string database)
        {
            var usage = SearchIndexStorage.ReadUsage(database);
            if (!usage.IsAvailable || usage.Bytes <= 0) throw new IOException("Fixture index size is unavailable.");
            var expected = Navigation.DriveCapacity.FormatBytes(usage.Bytes);
            for (var attempt = 0; attempt < 50; attempt++)
            {
                await page.RefreshUsageAsync();
                if (size.Text == expected) return;
                await Task.Delay(100);
            }
            throw new IOException($"Index size UI does not reflect disk usage: '{size.Text}', expected '{expected}'.");
        }
    }
}
#endif
