using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class FileDropPolicyTests
{
    [Fact]
    public void Returning_to_current_folder_is_a_no_op_even_for_copy()
    {
        var folder = Path.GetFullPath("drag-return");
        var source = Path.Combine(folder, "file.txt");
        Assert.Empty(FileDropPolicy.FilterSources([source], folder, move: false));
        Assert.Equal(FileDropOperation.None,
            FileDropPolicy.ResolveOperation([source], folder, true, false, false));
    }

    [Fact]
    public void Hovering_an_ordinary_file_is_not_a_background_drop()
    {
        var folder = Path.GetFullPath("drag-target");
        var source = Path.GetFullPath("drag-source/file.txt");
        Assert.Equal(FileDropOperation.None,
            FileDropPolicy.ResolveOperation([source], folder, validTarget: false, control: false, shift: false));
    }

    [Fact]
    public void Ctrl_explicitly_allows_a_same_directory_copy()
    {
        var folder = Path.GetFullPath("drag-return");
        var source = Path.Combine(folder, "file.txt");
        Assert.Equal(FileDropOperation.Copy,
            FileDropPolicy.ResolveOperation([source], folder, true, true, false));
        Assert.Single(FileDropPolicy.FilterSources([source], folder, false, allowSameDirectoryCopy: true));
    }

    [Fact]
    public void Default_same_volume_drag_moves_and_ctrl_copies()
    {
        var source = Path.GetFullPath("drag-source/file.txt");
        var target = Path.GetFullPath("drag-target");
        Assert.Equal(FileDropOperation.Move, FileDropPolicy.ResolveOperation([source], target, true, false, false));
        Assert.Equal(FileDropOperation.Copy, FileDropPolicy.ResolveOperation([source], target, true, true, false));
        Assert.Equal(FileDropOperation.Move, FileDropPolicy.ResolveOperation([source], target, true, false, true));
    }

    [Fact]
    public void Default_cross_volume_drag_copies()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(FileDropOperation.Copy,
            FileDropPolicy.ResolveOperation([@"C:\source\file.txt"], @"D:\target", true, false, false));
    }

    [Fact]
    public void Move_into_the_current_parent_is_a_no_op()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "note.txt");
            File.WriteAllText(source, "note");

            var accepted = FileDropPolicy.FilterSources([source], root, move: true);

            Assert.Empty(accepted);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Rejects_dropping_a_folder_into_its_own_descendant()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source");
            var child = Path.Combine(source, "child");
            Directory.CreateDirectory(child);

            var accepted = FileDropPolicy.FilterSources([source], child, move: false);

            Assert.Empty(accepted);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Keeps_unique_paths_that_can_be_copied_to_another_directory()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source.txt");
            var destination = Path.Combine(root, "destination");
            File.WriteAllText(source, "source");
            Directory.CreateDirectory(destination);

            var accepted = FileDropPolicy.FilterSources([source, source], destination, move: false);

            Assert.Equal([Path.GetFullPath(source)], accepted);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMateDropPolicy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
