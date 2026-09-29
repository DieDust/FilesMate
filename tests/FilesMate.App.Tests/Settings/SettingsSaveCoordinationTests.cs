using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.ViewModels;

namespace FilesMate.App.Tests.Settings;

public sealed class SettingsSaveCoordinationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Temporary_reader_lock_does_not_lose_a_setting(bool appearance)
    {
        var directory = Directory.CreateTempSubdirectory("FilesMate-settings-lock-");
        var path = Path.Combine(directory.FullName, "settings.json");
        try
        {
            File.WriteAllText(path, "{}");
            Task save;
            using (var reader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                save = appearance
                    ? new AppearanceSettingsService(path).SaveAsync(AppearanceSettings.Default with { Theme = AppThemeKind.Dark })
                    : new ExplorerPreferencesService(path).SaveAsync(ExplorerPreferences.Default with { ShowAlternatingRows = false });
                await Task.Delay(450);
            }
            await save;
            if (appearance) Assert.Equal(AppThemeKind.Dark, new AppearanceSettingsService(path).Load().Theme);
            else Assert.False(new ExplorerPreferencesService(path).Load().ShowAlternatingRows);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_save_preserves_old_settings_and_releases_the_queue(bool appearance)
    {
        var directory = Directory.CreateTempSubdirectory("FilesMate-settings-cancel-");
        var path = Path.Combine(directory.FullName, "settings.json");
        try
        {
            File.WriteAllText(path, "{}");
            var explorer = new ExplorerPreferencesService(path);
            var style = new AppearanceSettingsService(path);
            Task Save(CancellationToken token) => appearance
                ? style.SaveAsync(AppearanceSettings.Default with { Theme = AppThemeKind.Dark }, token)
                : explorer.SaveAsync(ExplorerPreferences.Default with { ShowAlternatingRows = false }, token);
            using (var reader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using var cancellation = new CancellationTokenSource(80);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Save(cancellation.Token));
                Assert.Equal("{}", File.ReadAllText(path));
            }
            await Save(CancellationToken.None);
            Assert.Single(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Rapid_appearance_changes_save_in_order_without_sharing_a_temporary_file()
    {
        var directory = Directory.CreateTempSubdirectory("FilesMate-settings-order-");
        try
        {
            var path = Path.Combine(directory.FullName, "appearance.json");
            var store = new AppearanceSettingsService(path);
            var values = Enumerable.Range(0, 20).Select(i => AppearanceSettings.Default with { TransparencyPercent = i + 30 }).ToArray();
            await Task.WhenAll(values.Select(value => store.SaveAsync(value)));
            Assert.Equal(values[^1], store.Load());
            Assert.Single(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task An_older_failed_change_cannot_roll_back_a_newer_theme()
    {
        var store = new DelayedAppearanceStore();
        var applied = new List<AppearanceSettings>();
        var model = new AppearanceSettingsViewModel(store, AppearanceSettings.Default, applied.Add);
        var first = model.SetThemeAsync(AppThemeKind.Dark);
        var second = model.SetFileTypographyAsync("Arial", 16, 13);
        store.Saves[1].SetResult();
        await second;
        store.Saves[0].SetException(new IOException("Earlier save failed"));
        await first;
        Assert.Equal(AppThemeKind.Dark, model.Current.Theme);
        Assert.Equal("Arial", model.Current.FileFontFamily);
        Assert.Equal(model.Current, applied[^1]);
        Assert.Null(model.ErrorText);
    }

    private sealed class DelayedAppearanceStore : IAppearanceSettingsService
    {
        internal List<TaskCompletionSource> Saves { get; } = [];
        public AppearanceSettings Load() => AppearanceSettings.Default;
        public Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Saves.Add(completion);
            return completion.Task;
        }
    }
}
