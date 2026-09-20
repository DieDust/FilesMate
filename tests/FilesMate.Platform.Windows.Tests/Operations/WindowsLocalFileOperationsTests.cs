using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class WindowsLocalFileOperationsTests
{
    [Fact]
    public void New_folder_has_only_one_creator_and_never_claims_an_existing_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-create-" + Guid.NewGuid().ToString("N"));
        var operations = new WindowsLocalFileOperations();
        var created = 0;
        try
        {
            Parallel.For(0, 16, _ =>
            {
                try { operations.CreateDirectory(root, failIfExists: true); Interlocked.Increment(ref created); }
                catch (IOException) { }
            });
            Assert.Equal(1, created);
            var sentinel = Path.Combine(root, "existing.txt");
            File.WriteAllText(sentinel, "keep");
            Assert.Throws<IOException>(() => operations.CreateDirectory(root, failIfExists: true));
            operations.CreateDirectory(root); // Undo can still ensure that a parent exists.
            Assert.Equal("keep", File.ReadAllText(sentinel));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void New_file_cannot_truncate_an_existing_file_even_after_a_name_collision()
    {
        var file = Path.Combine(Path.GetTempPath(), "FilesMate-create-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(file, "Existing user content must survive.");
            Assert.Throws<IOException>(() => new WindowsLocalFileOperations().CreateEmptyFile(file));
            Assert.Equal("Existing user content must survive.", File.ReadAllText(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Create_copy_rename_and_permanent_delete_work_on_disk()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMateOps", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ops = new WindowsLocalFileOperations();
        try
        {
            var folder = Path.Combine(root, "New folder");
            ops.CreateDirectory(folder);
            var file = Path.Combine(folder, "note.txt");
            ops.CreateEmptyFile(file);
            Assert.True(File.Exists(file));

            var renamed = Path.Combine(folder, "renamed.txt");
            ops.Rename(file, renamed);
            Assert.True(File.Exists(renamed));
            Assert.False(File.Exists(file));

            var copyRoot = Path.Combine(root, "copy");
            ops.Copy([renamed], copyRoot);
            Assert.True(File.Exists(Path.Combine(copyRoot, "renamed.txt")));

            ops.PermanentDelete([renamed]);
            Assert.False(File.Exists(renamed));

            var locked = Path.Combine(folder, "locked");
            Directory.CreateDirectory(locked);
            var readOnly = Path.Combine(locked, "note.txt");
            File.WriteAllText(readOnly, "x");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(locked);
                ops.PermanentDelete([locked]);
            }
            finally
            {
                if (Directory.Exists(previous))
                {
                    Directory.SetCurrentDirectory(previous);
                }
            }

            Assert.False(Directory.Exists(locked));

            var watched = Path.Combine(folder, "watched");
            Directory.CreateDirectory(watched);
            using var watcher = new FileSystemWatcher(watched) { EnableRaisingEvents = true };
            ops.PermanentDelete([watched]);
            Assert.False(Directory.Exists(watched));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
