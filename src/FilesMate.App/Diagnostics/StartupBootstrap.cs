using System.Runtime.CompilerServices;

namespace FilesMate.App.Diagnostics;

internal static class StartupBootstrap
{
    [ModuleInitializer]
    internal static void Initialize() => StartupClock.Mark("ProcessEntry");
}
