using FilesMate.App.Services;

namespace FilesMate.App.Tests.Settings;

public sealed class UpdateSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FilesMate-update-settings-" + Guid.NewGuid().ToString("N"));
    private UpdateSettingsStore Store => new(Path.Combine(_directory, "updates.json"));

    [Fact]
    public void Automatic_check_reserves_once_per_calendar_day_across_store_instances()
    {
        var day = new DateOnly(2026, 9, 16);
        Assert.True(Store.TryReserveDailyCheck(day));
        Assert.False(Store.TryReserveDailyCheck(day));
        Assert.True(Store.TryReserveDailyCheck(day.AddDays(1)));
    }

    [Fact]
    public void Disabled_automatic_checks_still_allow_manual_retry()
    {
        Store.SetAutomatic(false);
        var day = new DateOnly(2026, 9, 16);
        Assert.False(Store.TryReserveDailyCheck(day));
        Assert.True(Store.TryReserveDailyCheck(day, manual: true));
        Assert.True(Store.TryReserveDailyCheck(day, manual: true));
        Assert.False(Store.Load().AutomaticallyCheck);
    }

    [Fact]
    public void Concurrent_reservations_have_one_winner()
    {
        var count = 0;
        Parallel.For(0, 20, _ => { if (Store.TryReserveDailyCheck(new(2026, 9, 16))) Interlocked.Increment(ref count); });
        Assert.Equal(1, count);
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
