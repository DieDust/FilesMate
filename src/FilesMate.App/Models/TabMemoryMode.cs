namespace FilesMate.App.Models;

public enum TabMemoryMode { Off, Moderate, Balanced, Aggressive }

public static class TabMemoryPolicy
{
    public static TimeSpan IdleDelay(TabMemoryMode mode) => mode switch
    {
        TabMemoryMode.Moderate => TimeSpan.FromMinutes(30),
        TabMemoryMode.Balanced => TimeSpan.FromMinutes(10),
        TabMemoryMode.Aggressive => TimeSpan.FromMinutes(2),
        _ => TimeSpan.MaxValue,
    };

    public static bool ShouldHibernate(TabMemoryMode mode, TimeSpan idle, bool active, bool protectedTab, bool busy) =>
        mode != TabMemoryMode.Off && !active && !protectedTab && !busy && idle >= IdleDelay(mode);
}
