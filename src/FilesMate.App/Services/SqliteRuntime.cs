using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Services;

internal static class SqliteRuntime
{
    internal static void InitializeBeforeUi()
    {
        // SqliteConnection probes Windows.Storage.ApplicationData.Current in
        // its static constructor. On an STA that COM activation pumps messages;
        // during XAML Loaded this can reenter layout and fail-fast. Finish the
        // process-wide initializer on an MTA before Application.Start creates UI.
        Task.Run(static () => RuntimeHelpers.RunClassConstructor(typeof(SqliteConnection).TypeHandle))
            .GetAwaiter().GetResult();
    }
}
