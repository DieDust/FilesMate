using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

internal static class MediaCacheSmoke
{
    internal static async Task<int> Run(string fixture, string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var type = typeof(FilesMate.SearchHost.Program).Assembly.GetType("FilesMate.SearchHost.SearchMediaPreview")!;
            var instance = Activator.CreateInstance(type, true)!;
            var method = type.GetMethod("LoadAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Task<BitmapSource?> Load(CancellationToken token = default) => (Task<BitmapSource?>)method.Invoke(instance, new object[] { fixture, 512, token })!;
            var watch = Stopwatch.StartNew();
            var first = await Load();
            var cold = watch.Elapsed.TotalMilliseconds;
            if (first is null || !first.IsFrozen || first.PixelWidth > 512 || first.PixelHeight > 512) throw new Exception("Missing bounded static thumbnail");
            watch.Restart();
            for (var i = 0; i < 20; i++)
                if (!ReferenceEquals(first, await Load())) throw new Exception("Repeated thumbnail created a new bitmap");
            var warm = watch.Elapsed.TotalMilliseconds / 20;
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await Load(cancellation.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            // This is an isolated generated fixture, not a user document.
            File.SetLastWriteTimeUtc(fixture, File.GetLastWriteTimeUtc(fixture).AddSeconds(2));
            var updated = await Load();
            if (updated is null || ReferenceEquals(first, updated)) throw new Exception("Modified file reused stale bitmap");
            type.GetMethod("Clear", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, null);
            if (ReferenceEquals(updated, await Load())) throw new Exception("Cleared cache retained rendered bitmap");
            File.WriteAllText(Path.Combine(output,"result.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed=true, ColdMs=cold, WarmMeanMs=warm, ReusedBitmapCount=20, ModifiedFileInvalidation=true, Cancellation=true, Clear=true }));
            return 0;
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(output,"failure.txt"), error.ToString()); return 1; }
    }
}
