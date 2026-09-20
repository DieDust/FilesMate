using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class LaunchPathTests
{
    [Theory]
    [InlineData("--settings-search")]
    [InlineData("/settings-search")]
    public void Search_settings_launch_is_distinct_from_empty_activation(string argument)
    {
        Assert.Equal(new LaunchTarget(null, null, "search"), LaunchPath.Parse([argument]));
        Assert.True(LaunchPath.Parse(["--activate"]).ActivateOnly);
    }

    [Fact]
    public void Explicit_open_of_a_vanished_path_reports_it_instead_of_doing_nothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"), "gone");
        foreach (var form in new string[][] { ["/open", missing], ["/open," + missing], ["/select," + missing], ["/select", missing] })
        {
            var target = LaunchPath.Parse(form);
            Assert.Null(target.Folder);
            Assert.Null(target.SelectPath);
            Assert.Equal(Path.GetFullPath(missing), target.MissingPath);
        }

        // Plain positional arguments are not requests to open anything in particular: flags and stray words stay silent.
        Assert.Null(LaunchPath.Parse(["FilesMate.App.exe", "-foo", "nothing-here"]).MissingPath);
        Assert.Null(LaunchPath.Parse(["/open", "relative\\path"]).MissingPath);
        Assert.Null(LaunchPath.Parse(["--activate"]).MissingPath);
    }

    [Fact]
    public void Consecutive_files_in_same_folder_are_not_duplicate_destinations()
    {
        var first = new LaunchTarget(@"D:\Docs", @"D:\Docs\first.txt");
        Assert.False(first.SameDestination(new(@"D:\Docs", @"D:\Docs\second.txt")));
        Assert.True(first.SameDestination(new(@"d:\docs", @"d:\docs\FIRST.txt")));
        Assert.False(new LaunchTarget(null, null).SameDestination(new(null, null, "search")));
        Assert.False(new LaunchTarget(null, null).SameDestination(new(null, null, ActivateOnly: true)));
    }
    [Fact]
    public void Select_folder_reveals_it_in_its_parent_for_explorer_argument_forms()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "中文 folder");
        Directory.CreateDirectory(child);
        try
        {
            string[][] forms = [["/select," + child], ["/select,", child], ["/select", child], ["/e,/select," + child], ["/select," + child + "\\"]];
            foreach (var form in forms)
            {
                var target = LaunchPath.Parse(form);
                Assert.Equal(root, target.Folder);
                Assert.Equal(child, target.SelectPath);
            }

            Assert.Equal(new LaunchTarget(child, null), LaunchPath.Parse(["/open", child]));
            Assert.Equal(new LaunchTarget(child, null), LaunchPath.Parse([new Uri(child).AbsoluteUri]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryFolder_returns_existing_directory_and_skips_flags()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "note.txt");
        File.WriteAllText(file, "ok");
        try
        {
            Assert.Null(LaunchPath.TryFolder(["FilesMate.App.exe"]));
            Assert.Equal(
                Path.GetFullPath(directory),
                LaunchPath.TryFolder(["FilesMate.App.exe", "-foo", directory]));
            Assert.Equal(
                Path.GetFullPath(directory),
                LaunchPath.TryFolder(["FilesMate.App.exe", "/select," + file]));
            Assert.Equal(
                Path.GetFullPath(directory),
                LaunchPath.TryFolder(["FilesMate.App.exe", "/e," + directory]));
            Assert.Equal(
                Path.GetFullPath(directory),
                LaunchPath.TryFolder(["FilesMate.App.exe", "/select", file]));
            var root = Path.GetPathRoot(directory);
            Assert.False(string.IsNullOrEmpty(root));
            Assert.Equal(
                Path.GetFullPath(root),
                LaunchPath.TryFolder(["FilesMate.App.exe", root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)]));
            Assert.Equal(
                HomeLocation.Uri,
                LaunchPath.TryFolder(["FilesMate.App.exe", HomeLocation.Uri]));
            Assert.Equal(
                TagLocation.Uri(3),
                LaunchPath.TryFolder(["FilesMate.App.exe", "filesmate:tag:3"]));
            var select = LaunchPath.Parse(["FilesMate.App.exe", "/select," + file]);
            Assert.Equal(Path.GetFullPath(directory), select.Folder);
            Assert.Equal(Path.GetFullPath(file), select.SelectPath);
            var fromMainArgs = LaunchPath.Parse([directory]);
            Assert.Equal(Path.GetFullPath(directory), fromMainArgs.Folder);
            var revealFile = LaunchPath.Parse(["FilesMate.App.exe", file]);
            Assert.Equal(Path.GetFullPath(directory), revealFile.Folder);
            Assert.Equal(Path.GetFullPath(file), revealFile.SelectPath);
            Assert.False(LaunchPath.IsExplorerHost(["FilesMate.App.exe"]));
            Assert.False(LaunchPath.IsExplorerHost(["FilesMate.App.exe", directory]));
            Assert.True(LaunchPath.IsExplorerHost(
                ["FilesMate.App.exe", @"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"]));
            Assert.True(LaunchPath.IsExplorerHost(["FilesMate.App.exe", "shell:MyComputerFolder"]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Parse_skips_host_executable_instead_of_opening_its_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var hostDirectory = Path.Combine(root, "win-x64");
        var payloadDirectory = Path.Combine(root, "downloads");
        Directory.CreateDirectory(hostDirectory);
        Directory.CreateDirectory(payloadDirectory);
        var host = Path.Combine(hostDirectory, "FilesMate.App.exe");
        var file = Path.Combine(payloadDirectory, "Stonewards.rar");
        File.WriteAllBytes(host, [0]);
        File.WriteAllText(file, "ok");
        try
        {
            var duplicatedHost = LaunchPath.Parse([host, host, file]);
            Assert.Equal(Path.GetFullPath(payloadDirectory), duplicatedHost.Folder);
            Assert.Equal(Path.GetFullPath(file), duplicatedHost.SelectPath);

            var hostThenFolder = LaunchPath.Parse(["FilesMate.App.exe", host, payloadDirectory]);
            Assert.Equal(Path.GetFullPath(payloadDirectory), hostThenFolder.Folder);
            Assert.Null(hostThenFolder.SelectPath);

            var hostOnly = LaunchPath.Parse([host, host]);
            Assert.Null(hostOnly.Folder);
            Assert.Null(hostOnly.SelectPath);

            var hostFolder = LaunchPath.Parse([host, hostDirectory]);
            Assert.Null(hostFolder.Folder);
            Assert.Null(hostFolder.SelectPath);

            var explicitHostFolder = LaunchPath.Parse([host, "/open", hostDirectory]);
            Assert.Equal(Path.GetFullPath(hostDirectory), explicitHostFolder.Folder);
            Assert.Null(explicitHostFolder.SelectPath);

            var openDownloads = LaunchPath.Parse([host, "/open", payloadDirectory]);
            Assert.Equal(Path.GetFullPath(payloadDirectory), openDownloads.Folder);

            var dll = Path.Combine(hostDirectory, "FilesMate.App.dll");
            File.WriteAllBytes(dll, [0]);
            var fromDll = LaunchPath.Parse([dll]);
            Assert.Null(fromDll.Folder);
            Assert.Null(fromDll.SelectPath);

            var explorerSelect = LaunchPath.Parse([host, "/select," + file]);
            Assert.Equal(Path.GetFullPath(payloadDirectory), explorerSelect.Folder);
            Assert.Equal(Path.GetFullPath(file), explorerSelect.SelectPath);

            Assert.Equal([file], LaunchPath.SplitActivationArguments('"' + file + '"'));
            Assert.Equal(
                [host, file],
                LaunchPath.SplitActivationArguments($"\"{host}\" \"{file}\""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
