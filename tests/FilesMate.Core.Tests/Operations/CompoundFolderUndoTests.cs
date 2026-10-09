using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class CompoundFolderUndoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Moving_same_named_folders_then_merging_has_repeatable_undo_and_redo(bool directoryChild)
    {
        using var fixture = new Fixture(directoryChild);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Assert.True(fixture.Stack.TryUndo(fixture.Operations)); fixture.AssertOriginal();
            Assert.True(fixture.Stack.TryRedo(fixture.Operations)); fixture.AssertMerged();
        }
    }

    [Theory]
    [InlineData(1, false, false)] [InlineData(2, false, false)] [InlineData(3, false, false)] [InlineData(4, false, false)]
    [InlineData(1, true, false)] [InlineData(2, true, false)] [InlineData(3, true, false)] [InlineData(4, true, false)]
    [InlineData(1, false, true)] [InlineData(2, false, true)] [InlineData(3, false, true)] [InlineData(4, false, true)]
    [InlineData(1, true, true)] [InlineData(2, true, true)] [InlineData(3, true, true)] [InlineData(4, true, true)]
    public void Every_staging_and_publication_failure_rolls_back_and_remains_retryable(int failAt, bool redo, bool cancel)
    {
        using var fixture = new Fixture(directoryChild: true);
        if (redo) Assert.True(fixture.Stack.TryUndo(fixture.Operations));
        fixture.Operations.Calls = 0;
        fixture.Operations.FailOn = [failAt];
        fixture.Operations.Cancel = cancel;
        var error = Record.Exception(() => fixture.Apply(redo));
        if (cancel) Assert.IsType<OperationCanceledException>(error); else Assert.IsType<IOException>(error);
        if (redo) fixture.AssertOriginal(); else fixture.AssertMerged(allowEmptySource: true);
        fixture.Operations.FailOn = [];
        Assert.True(fixture.Apply(redo));
        if (redo) fixture.AssertMerged(); else fixture.AssertOriginal();
    }

    [Theory]
    [InlineData(false, 5)] [InlineData(false, 6)] [InlineData(false, 7)]
    [InlineData(true, 5)] [InlineData(true, 6)] [InlineData(true, 7)]
    public void Recovery_failure_reports_retained_paths_and_keeps_every_content_version(bool redo, int recoveryFailAt)
    {
        using var fixture = new Fixture(directoryChild: true);
        if (redo) Assert.True(fixture.Stack.TryUndo(fixture.Operations));
        fixture.Operations.Calls = 0; fixture.Operations.FailOn = [4, recoveryFailAt];
        var error = Assert.Throws<IOException>(() => fixture.Apply(redo));
        Assert.Contains(fixture.Root, error.Message);
        var contents = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Select(File.ReadAllText).OrderBy(value => value).ToArray();
        Assert.Equal(new[] { "first", "second" }, contents);
    }

    [Fact]
    public void Three_source_folders_with_nested_ownership_round_trip_without_moving_another_sources_content()
    {
        using var fixture = new Fixture(directoryChild: true);
        var (third, target, thirdRoot) = AddThirdSource(fixture);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Assert.True(fixture.Stack.TryUndo(fixture.Operations));
            fixture.AssertOriginal(); Assert.Equal("third", File.ReadAllText(Path.Combine(third, "c.txt")));
            Assert.True(fixture.Stack.TryRedo(fixture.Operations));
            fixture.AssertMerged(); Assert.Equal("third", File.ReadAllText(Path.Combine(target, "c.txt")));
            Assert.False(Directory.Exists(thirdRoot));
        }
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    [InlineData(4, false, false)]
    [InlineData(5, false, false)]
    [InlineData(6, false, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(3, false, true)]
    [InlineData(4, false, true)]
    [InlineData(5, false, true)]
    [InlineData(6, false, true)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(4, true, false)]
    [InlineData(5, true, false)]
    [InlineData(6, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, true)]
    [InlineData(4, true, true)]
    [InlineData(5, true, true)]
    [InlineData(6, true, true)]
    public void Three_level_ownership_recovers_every_rename_boundary(int failAt, bool redo, bool cancel)
    {
        using var fixture = new Fixture(directoryChild: true);
        var (third, target, _) = AddThirdSource(fixture);
        if (redo) Assert.True(fixture.Stack.TryUndo(fixture.Operations));
        fixture.Operations.Calls = 0; fixture.Operations.FailOn = [failAt]; fixture.Operations.Cancel = cancel;
        var error = Record.Exception(() => fixture.Apply(redo));
        if (cancel) Assert.IsType<OperationCanceledException>(error); else Assert.IsType<IOException>(error);
        if (redo) fixture.AssertOriginal(); else fixture.AssertMerged(allowEmptySource: true);
        Assert.Equal("third", File.ReadAllText(Path.Combine(redo ? third : target, "c.txt")));
        fixture.Operations.FailOn = [];
        Assert.True(fixture.Apply(redo));
        if (redo) fixture.AssertMerged(); else fixture.AssertOriginal();
        Assert.Equal("third", File.ReadAllText(Path.Combine(redo ? target : third, "c.txt")));
    }

    private static (string Original, string Target, string Root) AddThirdSource(Fixture fixture)
    {
        var third = Directory.CreateDirectory(Path.Combine(fixture.Root, "in3", "same", "nested", "extra")).FullName;
        File.WriteAllText(Path.Combine(third, "c.txt"), "third");
        var target = Path.Combine(fixture.Destination, "nested", "extra");
        Directory.Move(third, target);
        var thirdParent = Path.GetDirectoryName(third)!;
        var thirdRoot = Path.GetDirectoryName(thirdParent)!;
        Directory.Delete(thirdParent); Directory.Delete(thirdRoot);
        var old = fixture.Stack.Latest!;
        fixture.Stack.Clear();
        fixture.Stack.Push(FileUndoRecord.Merged([.. old.Paths, thirdParent, thirdRoot], [.. old.Pairs, new(third, target)]));
        return (third, target, thirdRoot);
    }

    [Fact]
    public void Rollback_timestamp_acceptance_never_adopts_an_unrelated_file_edit()
    {
        using var fixture = new Fixture(directoryChild: true);
        fixture.Operations.FailOn = [2];
        fixture.Operations.BeforeFailure = () => File.WriteAllText(Path.Combine(fixture.Destination, "a.txt"), "foreign edit");
        Assert.Throws<IOException>(() => fixture.Stack.TryUndo(fixture.Operations));
        fixture.Operations.FailOn = [];
        Assert.Throws<UndoStateChangedException>(() => fixture.Stack.TryUndo(fixture.Operations));
        Assert.Equal("foreign edit", File.ReadAllText(Path.Combine(fixture.Destination, "a.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(fixture.Destination, fixture.Child)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Replacement_inside_a_moved_root_has_repeatable_history_and_rejects_external_changes(bool copyReplacement)
    {
        using var fixture = new Fixture(replacement: true, copyReplacement: copyReplacement);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Assert.True(fixture.Stack.TryUndo(fixture.Operations)); fixture.AssertOriginal();
            Assert.True(fixture.Stack.TryRedo(fixture.Operations)); fixture.AssertMerged();
        }
        File.WriteAllText(Path.Combine(fixture.Destination, "foreign.txt"), "do not adopt");
        Assert.Throws<UndoStateChangedException>(() => fixture.Stack.TryUndo(fixture.Operations));
        Assert.Equal("second", File.ReadAllText(Path.Combine(fixture.Destination, "a.txt")));
        Assert.Equal("do not adopt", File.ReadAllText(Path.Combine(fixture.Destination, "foreign.txt")));
    }

    [Fact]
    public void Accepting_a_nested_replacement_does_not_recapture_other_entries()
    {
        using var fixture = new Fixture(replacement: true);
        var record = fixture.Stack.Latest!;
        File.WriteAllText(Path.Combine(fixture.Destination, "foreign.txt"), "external");
        var replacement = Assert.Single(record.Replacements);
        var previousBackup = replacement.Backup;
        replacement.Undo();
        typeof(FileUndoRecord).GetMethod("AcceptReplacementState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(record, [replacement, previousBackup]);
        Assert.Throws<UndoStateChangedException>(() => fixture.Stack.TryUndo(fixture.Operations));
        Assert.Equal("external", File.ReadAllText(Path.Combine(fixture.Destination, "foreign.txt")));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FilesMate.CompoundUndo", Guid.NewGuid().ToString("N"));
        public string First { get; }
        public string Second { get; }
        public string Destination { get; }
        public string Child { get; }
        public FileUndoStack Stack { get; } = new();
        public Operations Operations { get; } = new();
        private readonly bool _replacement;
        private readonly bool _copyReplacement;
        private readonly DateTime _stamp = new(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        public Fixture(bool directoryChild = false, bool replacement = false, bool copyReplacement = false)
        {
            _replacement = replacement; _copyReplacement = copyReplacement;
            First = Path.Combine(Root, "in1", "same"); Second = Path.Combine(Root, "in2", "same"); Destination = Path.Combine(Root, "out", "same");
            Directory.CreateDirectory(First); Directory.CreateDirectory(Second); Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            Child = replacement ? "a.txt" : directoryChild ? Path.Combine("nested", "deep", "b.txt") : "b.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Second, Child))!);
            File.WriteAllText(Path.Combine(First, "a.txt"), "first"); File.SetLastWriteTimeUtc(Path.Combine(First, "a.txt"), _stamp);
            File.WriteAllText(Path.Combine(Second, Child), "second"); File.SetLastWriteTimeUtc(Path.Combine(Second, Child), _stamp);
            Directory.Move(First, Destination);
            FileUndoRecord record;
            if (replacement)
            {
                var backup = Path.Combine(Directory.CreateDirectory(Path.Combine(Destination, ".filesmate-history-test")).FullName, "a.txt");
                var incoming = Path.Combine(Second, Child);
                if (copyReplacement) { incoming += ".staged"; File.Copy(Path.Combine(Second, Child), incoming); }
                File.Replace(incoming, Path.Combine(Destination, Child), backup);
                if (!copyReplacement) Directory.Delete(Second, recursive: false);
                record = FileUndoRecord.Merged(copyReplacement ? [] : [Second], [new(First, Destination)]) with
                { Replacements = [new FileReplacement(Path.Combine(Second, Child), Path.Combine(Destination, Child), backup, !copyReplacement)] };
            }
            else
            {
                var source = directoryChild ? Path.Combine(Second, "nested") : Path.Combine(Second, Child);
                var target = directoryChild ? Path.Combine(Destination, "nested") : Path.Combine(Destination, Child);
                Operations.Rename(source, target); Directory.Delete(Second, recursive: false);
                record = FileUndoRecord.Merged([Second], [new(First, Destination), new(source, target)]);
            }
            Stack.Push(record); Operations.Calls = 0;
        }
        public bool Apply(bool redo) => redo ? Stack.TryRedo(Operations) : Stack.TryUndo(Operations);
        public void AssertOriginal()
        {
            Assert.Equal("first", File.ReadAllText(Path.Combine(First, "a.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(Second, Child)));
            Assert.Equal(_stamp, File.GetLastWriteTimeUtc(Path.Combine(First, "a.txt")));
            Assert.Equal(_stamp, File.GetLastWriteTimeUtc(Path.Combine(Second, Child)));
            Assert.False(Directory.Exists(Destination)); AssertNoStaging();
        }
        public void AssertMerged(bool allowEmptySource = false)
        {
            Assert.False(Directory.Exists(First));
            Assert.Equal(_replacement ? "second" : "first", File.ReadAllText(Path.Combine(Destination, "a.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(Destination, Child)));
            Assert.Equal(_stamp, File.GetLastWriteTimeUtc(Path.Combine(Destination, Child)));
            if (!_copyReplacement)
            {
                if (allowEmptySource && Directory.Exists(Second)) Assert.Empty(Directory.EnumerateFileSystemEntries(Second));
                else Assert.False(Directory.Exists(Second));
            }
            AssertNoStaging();
        }
        private void AssertNoStaging() => Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories), path => Path.GetFileName(path).StartsWith(".filesmate-undo-", StringComparison.Ordinal));
        public void Dispose()
        {
            // Do not dispose a replacement after moving its owner root in this test. The
            // guarded synthetic fixture cleanup owns every retained version and path.
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.CompoundUndo")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(Root).StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
    private sealed class Operations : ILocalFileOperations
    {
        public bool RequiresUndoValidation => true;
        public int Calls { get; set; }
        public HashSet<int> FailOn { get; set; } = [];
        public bool Cancel { get; set; }
        public Action? BeforeFailure { get; set; }
        public void Rename(string source, string destination)
        {
            if (FailOn.Contains(++Calls)) { BeforeFailure?.Invoke(); if (Cancel) throw new OperationCanceledException(); throw new IOException("Injected rename failure"); }
            if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
        }
        public void CreateDirectory(string path, bool failIfExists = false) => Directory.CreateDirectory(path);
        public void CreateEmptyFile(string path) => throw new NotSupportedException();
        public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destination) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destination) => throw new NotSupportedException();
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null) => throw new NotSupportedException();
        public void RestoreRecycled(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
