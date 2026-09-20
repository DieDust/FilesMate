using System.Text.Json;

namespace FilesMate.Core.Operations;

/// <summary>Accounts for retained versions, including unfinished work and previous-process remnants.</summary>
public sealed class ReplacementBackupBudget(long capacity = 256L * 1024 * 1024, long maximumFile = 64L * 1024 * 1024)
{
    public static ReplacementBackupBudget Shared { get; } = new();
    public long Capacity { get; } = capacity;
    public long MaximumFile { get; } = maximumFile;
    private readonly object _sync = new();
    private readonly Dictionary<string, BackupReservation> _entries = new(StringComparer.OrdinalIgnoreCase);
    private string? _journalDirectory;
    public long UsedBytes { get { lock (_sync) return _entries.Values.Sum(entry => entry.Bytes); } }
    public IReadOnlyList<BackupReservation> Entries { get { lock (_sync) return _entries.Values.ToArray(); } }

    public void Initialize(string journalDirectory)
    {
        lock (_sync)
        {
            _journalDirectory = journalDirectory;
            Directory.CreateDirectory(journalDirectory);
            using var gate = OpenGate();
            Reload();
        }
    }

    private FileStream? OpenGate() => _journalDirectory is null ? null
        : new FileStream(Path.Combine(_journalDirectory, "budget.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private void Reload()
    {
        if (_journalDirectory is null)
        {
            foreach (var path in _entries.Keys.Where(ConfirmedMissing).ToArray()) _entries.Remove(path);
            return;
        }
        var loaded = new Dictionary<string, BackupReservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_journalDirectory, "*.json"))
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length > 32768) throw new IOException("Invalid undo backup journal size.");
                BackupReservation entry;
                try { entry = JsonSerializer.Deserialize<BackupReservation>(file) ?? throw new IOException("Invalid undo backup journal."); }
                catch (JsonException error) { throw new IOException("Invalid undo backup journal.", error); }
                if (entry.Bytes < 0 || entry.Bytes > 64L * 1024 * 1024 || string.IsNullOrWhiteSpace(entry.Directory) || !Path.IsPathFullyQualified(entry.Directory)
                    || !Path.GetFileName(entry.Directory).StartsWith(".filesmate-history-", StringComparison.Ordinal))
                    throw new IOException("Invalid undo backup location.");
                if (ConfirmedMissing(entry.Directory)) { file.Dispose(); File.Delete(path); continue; }
                loaded.TryAdd(entry.Directory, entry);
            }
        _entries.Clear();
        foreach (var entry in loaded) _entries.Add(entry.Key, entry.Value);
    }

    public bool CanReserve(long bytes)
    {
        lock (_sync)
        {
            try { using var gate = OpenGate(); Reload(); return Fits(bytes); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
        }
    }

    private bool Fits(long bytes) => _entries.Count < 256 && bytes >= 0 && bytes <= MaximumFile && bytes <= Capacity - UsedBytes;

    public IDisposable Reserve(string directory, long bytes, Action createDirectory)
    {
        lock (_sync)
        {
            using var gate = OpenGate();
            Reload();
            if (!Fits(bytes)) throw new IOException("Undo backup limit reached. Choose again without an undo backup.");
            var entry = new BackupReservation(directory, bytes, DateTimeOffset.UtcNow);
            createDirectory();
            try
            {
                if (_journalDirectory is not null)
                {
                    var pending = JournalPath(directory) + ".pending-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            JsonSerializer.Serialize(file, entry);
                            file.Flush(flushToDisk: true);
                        }
                        File.Move(pending, JournalPath(directory), overwrite: false);
                    }
                    finally { if (File.Exists(pending)) File.Delete(pending); }
                }
            }
            catch
            {
                Directory.Delete(directory, recursive: false);
                throw;
            }
            _entries.Add(directory, entry);
            return new Lease(this, directory);
        }
    }

    public void ForgetMissing()
    {
        lock (_sync)
            foreach (var path in _entries.Keys.ToArray()) Release(path);
    }

    private string JournalPath(string directory) => Path.Combine(_journalDirectory!, Path.GetFileName(directory) + ".json");

    private static bool ConfirmedMissing(string directory)
    {
        // An offline drive or inaccessible folder is not proof that a backup was deleted.
        if (!Directory.Exists(Path.GetPathRoot(directory))) return false;
        try { File.GetAttributes(directory); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    private void Release(string directory)
    {
        lock (_sync)
        {
            // A failed cleanup/recovery must remain visible and continue consuming its budget.
            if (!ConfirmedMissing(directory)) return;
            using var gate = OpenGate();
            if (_journalDirectory is not null) File.Delete(JournalPath(directory));
            _entries.Remove(directory);
        }
    }

    private sealed class Lease(ReplacementBackupBudget owner, string directory) : IDisposable
    {
        public void Dispose() => owner.Release(directory);
    }
}

public sealed record BackupReservation(string Directory, long Bytes, DateTimeOffset CreatedAt);
