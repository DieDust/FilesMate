namespace FilesMate.App.Models;

public static class SearchIndexFreshness
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    public static readonly TimeSpan RecheckEvery = TimeSpan.FromHours(6);

    public static bool ShouldRefresh(
        SearchIndexStats stats,
        DateTimeOffset now,
        bool autoRefresh,
        bool isRunning)
    {
        if (!autoRefresh || isRunning)
        {
            return false;
        }

        return stats.CompletedUtc is not { } done || now - done >= StaleAfter;
    }
}
