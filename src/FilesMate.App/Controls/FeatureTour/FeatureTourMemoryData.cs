using System.Runtime.CompilerServices;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Core.Recursive;

namespace FilesMate.App.Controls.FeatureTour;

// All enumeration/probing is explicitly injected. Unknown paths fail closed;
// the lexical root is only a tree identity and is never created or accessed.
internal sealed class FeatureTourMemoryData : IDirectoryEnumerator, IRecursiveDirectoryProbe, IDisposable
{
    private readonly Dictionary<string, FileEntryCore[]> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly RecursiveSession _session;
    public FeatureTourMemoryData(bool opening)
    {
        Root = System.IO.Path.Combine(AppContext.BaseDirectory, "__memory_tour__");
        var project = System.IO.Path.Combine(Root, "Project");
        var images = System.IO.Path.Combine(project, "Images");
        var today = DateTime.UtcNow.Date.Ticks;
        FileEntryCore Entry(string name, bool folder = false, ulong size = 640, bool old = false) =>
            new(0, name, folder ? 0 : size, old ? today - TimeSpan.TicksPerDay * 7 : today, today,
                folder ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Normal,
                folder ? EntryKind.Directory : EntryKind.File);
        _directories[Root] = [Entry("Project", true)];
        _directories[project] = opening ? [Entry("notes.txt")]
            : [Entry("notes.txt"), Entry("design.pdf", size: 800_000, old: true), Entry("Images", true)];
        if (!opening) _directories[images] = [Entry("cover.jpg", size: 2_400_000), Entry("cover-notes.txt")];
        _session = new(this, PaneId.New(), 1, Root, new RecursiveReadOptions
        {
            MaximumNodes = 16, MaximumDepth = 4,
            DirectoryOptions = new DirectoryReadOptions { IncludeHidden = true, IncludeSystem = true },
        }, directoryProbe: this);
    }
    public string Root { get; }
    public RecursiveSnapshot Snapshot => _session.CurrentSnapshot;
    public Task LoadAsync() => _session.RunAsync();
    public FileAttributes GetAttributes(string path) => _directories.ContainsKey(path)
        ? FileAttributes.Directory : throw new InvalidOperationException("Unknown memory directory.");
    public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(DirectoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_directories.TryGetValue(request.Path, out var entries)) throw new InvalidOperationException("Unknown memory directory.");
        await Task.CompletedTask;
        yield return DirectoryBatch.Create(request.PaneId, request.Generation, request.Path, entries, true, null);
    }
    public int Id(string name)
    {
        for (var id = 1; id <= Snapshot.Count; id++) if (Snapshot.GetEntry(id).Name == name) return id;
        return 0;
    }
    public void Dispose() => _session.Dispose();
}
