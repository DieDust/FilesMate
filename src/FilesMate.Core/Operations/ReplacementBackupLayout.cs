namespace FilesMate.Core.Operations;

/// <summary>Shared retained-backup layout; old absolute backup references remain valid.</summary>
public static class ReplacementBackupLayout
{
    public const string RootName = ".filesmate-history";
    private const string OwnerName = ".filesmate-owner";
    private const string OwnerContents = "FilesMate replacement backups v1";
    private static readonly object CreationGate = new();

    public static string EntryDirectory(string parent, Guid operation, Guid entry) =>
        Path.Combine(Path.GetFullPath(parent), RootName, operation.ToString("N"), entry.ToString("N"));

    public static bool IsRetainedLocation(string directory)
    {
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (leaf.StartsWith(".filesmate-history-", StringComparison.Ordinal)) return true;
        var batch = Path.GetDirectoryName(directory);
        var root = batch is null ? null : Path.GetDirectoryName(batch);
        return Guid.TryParseExact(leaf, "N", out _) && batch is not null
            && Guid.TryParseExact(Path.GetFileName(batch), "N", out _)
            && root is not null && string.Equals(Path.GetFileName(root), RootName, StringComparison.Ordinal);
    }

    public static string ContainerDirectory(string entryDirectory)
    {
        if (!IsRetainedLocation(entryDirectory)) throw new IOException("Invalid replacement backup location.");
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(entryDirectory)).StartsWith(".filesmate-history-", StringComparison.Ordinal)
            ? entryDirectory : Path.GetDirectoryName(Path.GetDirectoryName(entryDirectory))!;
    }

    public static bool IsOwnedRoot(string directory)
    {
        if (!string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)), RootName, StringComparison.OrdinalIgnoreCase)) return false;
        FileAttributes directoryAttributes;
        try { directoryAttributes = File.GetAttributes(directory); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((directoryAttributes & FileAttributes.Directory) == 0) return false;
        if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The backup root is not an ordinary directory.");
        var owner = Path.Combine(directory, OwnerName);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(owner); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("The backup ownership marker is not an ordinary file.");
        return new FileInfo(owner).Length <= 128 && File.ReadAllText(owner) == OwnerContents;
    }

    public static bool IsInsideOwnedRoot(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (IsOwnedRoot(current)) return true;
        return false;
    }

    private static void EnsureOwnedRoot(string root)
    {
        if (Directory.Exists(root))
        {
            if (!IsOwnedRoot(root)) throw new IOException("The backup folder is not owned by FilesMate.");
            return;
        }
        // Publish a fully marked private directory with a non-overwriting rename.
        // A different process or user winning the fixed name is never adopted.
        var preparing = Path.Combine(Path.GetDirectoryName(root)!, ".filesmate-history-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(preparing);
        RequireDirectory(preparing);
        var marker = Path.Combine(preparing, OwnerName);
        var markerOwned = false;
        try
        {
            using (var file = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                markerOwned = true;
                file.Write(System.Text.Encoding.UTF8.GetBytes(OwnerContents));
                file.Flush(flushToDisk: true);
            }
            try { Directory.Move(preparing, root); }
            catch (IOException) when (Directory.Exists(root))
            {
                if (!IsOwnedRoot(root)) throw new IOException("The backup folder is not owned by FilesMate.");
            }
        }
        finally
        {
            // A failed CreateNew never transfers another owner marker to us.
            if (markerOwned && Directory.Exists(preparing))
            {
                RequireDirectory(preparing);
                File.Delete(marker);
                Directory.Delete(preparing, recursive: false);
            }
        }
        if (!IsOwnedRoot(root)) throw new IOException("The backup folder is not owned by FilesMate.");
    }

    public static void CreateEntryDirectory(string directory)
    {
        if (!IsRetainedLocation(directory)) throw new IOException("Invalid replacement backup location.");
        var batch = Path.GetDirectoryName(directory)!;
        var root = Path.GetDirectoryName(batch)!;
        if (Path.GetFileName(root) != RootName) throw new IOException("New backups require the grouped layout.");
        lock (CreationGate)
        {
            EnsureOwnedRoot(root);
            Directory.CreateDirectory(batch);
            RequireDirectory(batch);
            if (Path.Exists(directory)) throw new IOException("Backup entry already exists.");
            Directory.CreateDirectory(directory);
            RequireDirectory(directory);
        }
    }

    private static void RequireDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new IOException("Replacement backups require an ordinary directory.");
    }

    public static void RemoveEmptyParents(string directory)
    {
        if (!IsRetainedLocation(directory)) return;
        var batch = Path.GetDirectoryName(directory);
        var root = batch is null ? null : Path.GetDirectoryName(batch);
        if (root is null || Path.GetFileName(root) != RootName || !IsOwnedRoot(root)) return;
        // Keep the fixed, owned root. Only empty batch containers are removed;
        // other batches and unrelated files are never recursively deleted.
        foreach (var path in new[] { batch! })
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                RequireDirectory(path);
                Directory.Delete(path, recursive: false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
