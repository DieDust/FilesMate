#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.Navigation;
using FilesMate.App.Diagnostics;
using Microsoft.UI.Xaml;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunDatabaseStartupSmokeAsync()
    {
        var reportPath = Path.Combine(AppContext.BaseDirectory, "database-startup-smoke.json");
        try
        {
            var marks = StartupClock.Snapshot().Select(mark => mark.Name).ToArray();
            var databaseReady = Array.IndexOf(marks, "DatabaseRuntimeReady");
            var appCreated = Array.IndexOf(marks, "AppConstructor");
            if (databaseReady < 0 || databaseReady >= appCreated)
                throw new InvalidOperationException("Database runtime was not initialized before XAML application construction.");

            var loaded = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (Content is FrameworkElement { IsLoaded: true } root &&
                    PolishDescendants(root).OfType<NavigationSidebar>().Any(sidebar => sidebar.IsLoaded))
                {
                    loaded = true;
                    break;
                }
                await Task.Delay(50);
            }
            if (!loaded) throw new InvalidOperationException("The actual navigation sidebar did not load.");

            var store = App.MetadataStore ?? throw new InvalidOperationException("Metadata store missing.");
            var before = await store.ListTagsAsync();
            var tag = await store.CreateTagAsync("startup-" + Guid.NewGuid().ToString("N"), "#4D7E70");
            try
            {
                if (!(await store.ListTagsAsync()).Any(item => item.Id == tag.Id))
                    throw new InvalidOperationException("Tag write/read failed after startup.");
            }
            finally { await store.DeleteTagAsync(tag.Id); }
            if ((await store.ListTagsAsync()).Count != before.Count)
                throw new InvalidOperationException("Tag removal did not restore the initial tag count.");

            var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(() => heartbeat.SetResult()))
                throw new InvalidOperationException("UI dispatcher rejected a callback.");
            await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                Passed = true,
                DatabaseReadyBeforeXaml = true,
                SidebarLoaded = loaded,
                TagReadWriteDelete = true,
                UiResponsive = true,
                NativeShell = Program.UseNativeShellHost,
                Marks = StartupClock.Snapshot(),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() }));
        }
    }
}
#endif
