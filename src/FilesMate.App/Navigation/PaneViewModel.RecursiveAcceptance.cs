using System.Text.Json;
using FilesMate.Core.Configuration;
using FilesMate.Core.Recursive;

namespace FilesMate.App.Navigation;

public sealed partial class PaneViewModel
{
    private static long _recursiveAcceptanceOperation;
    private static readonly SemaphoreSlim RecursiveAcceptanceCaptureGate = new(1, 1);
    private static long AcceptanceOperation(long operation) => operation != 0 || !RuntimeProfile.IsIsolated
        || !File.Exists(Path.Combine(RuntimeProfile.DirectoryPath, "recursive-acceptance.enabled"))
        ? operation : Interlocked.Increment(ref _recursiveAcceptanceOperation);
    // Explicit isolated correctness runs only. Keep full-path I/O off normal
    // launches and off performance runs; certificates identify published dumps.
    private static string? PrepareRecursiveAcceptanceDump(RecursiveProjection projection, long operation, CancellationToken cancellation)
    {
        if (!RuntimeProfile.IsIsolated || !File.Exists(Path.Combine(RuntimeProfile.DirectoryPath, "recursive-acceptance.enabled"))
            || projection.State == RecursiveSessionState.Loading) return null;
        var path = Path.Combine(RuntimeProfile.DirectoryPath, $"recursive-capture-{projection.SessionId:N}-{operation}.jsonl");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(JsonSerializer.Serialize(new { Kind = "Header", projection.SessionId, projection.Generation,
                projection.Revision, Operation = operation, projection.Snapshot.RootPath, SnapshotCount = projection.Snapshot.Count,
                VisibleCount = projection.Index.Count, State = projection.State.ToString(), projection.IsFiltering }));
            for (var id = 1; id <= projection.Snapshot.Count; id++)
            {
                cancellation.ThrowIfCancellationRequested();
                var node = projection.Snapshot.GetNode(id);
                writer.WriteLine(JsonSerializer.Serialize(new { Kind = "Snapshot", Id = id, node.ParentId, node.IsDirectory,
                    Path = projection.Snapshot.ResolveRelativePath(id), Visible = projection.IsVisible(id),
                    Expanded = projection.IsExpanded(id), ChildState = projection.GetChildState(id).ToString() }));
            }
            for (var view = 0; view < projection.Index.Count; view++)
            {
                cancellation.ThrowIfCancellationRequested();
                var id = projection.Index[view] + 1;
                writer.WriteLine(JsonSerializer.Serialize(new { Kind = "Visible", View = view, Id = id,
                    Path = projection.ResolveRelativePath(id) }));
            }
            return path;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { App.LogFailure("RecursiveAcceptanceDump", error); return null; }
    }

    private static void CaptureRecursiveAcceptancePublication(RecursiveProjection projection, long operation)
    {
        // Full-path diagnostics describe an already published immutable view.
        // They must never hold up its command, projection worker or native bind.
        // Drop overlapping diagnostic captures instead of retaining a queue of
        // large snapshots. Normal and performance profiles have no marker.
        if (!RuntimeProfile.IsIsolated || projection.State == RecursiveSessionState.Loading
            || !File.Exists(Path.Combine(RuntimeProfile.DirectoryPath, "recursive-acceptance.enabled"))
            || !RecursiveAcceptanceCaptureGate.Wait(0)) return;
        _ = Task.Run(() =>
        {
            try
            {
                var dump = PrepareRecursiveAcceptanceDump(projection, operation, CancellationToken.None);
                RecordRecursiveAcceptancePublication(projection, operation, dump);
            }
            catch (Exception error) { App.LogFailure("RecursiveAcceptanceCapture", error); }
            finally { RecursiveAcceptanceCaptureGate.Release(); }
        });
    }

    private static void RecordRecursiveAcceptancePublication(RecursiveProjection projection, long operation, string? dump)
    {
        if (dump is null) return;
        try
        {
            File.AppendAllText(Path.Combine(RuntimeProfile.DirectoryPath, "recursive-published.jsonl"),
                JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, projection.SessionId, projection.Generation,
                    projection.Revision, Operation = operation, projection.Snapshot.RootPath, VisibleCount = projection.Index.Count,
                    SnapshotCount = projection.Snapshot.Count, State = projection.State.ToString(), Dump = dump }) + Environment.NewLine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { App.LogFailure("RecursiveAcceptancePublication", error); }
    }
}
