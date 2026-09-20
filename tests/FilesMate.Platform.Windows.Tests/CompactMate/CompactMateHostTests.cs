using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;
using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Tests.CompactMate;

public sealed class CompactMateHostTests
{
    [Fact]
    public void PathsWithSpacesAndTrailingSlashSurviveNativeArgumentParsing()
    {
        var source = @"C:\my archive\";
        var destination = @"D:\my output\";
        var launch = CompactMateHost.Create(@"C:\CompactMate.exe", CompactMateVerb.CompressZip, [source], destination);
        var pointer = CommandLineToArgvW("CompactMate.exe " + launch.Arguments, out var count);
        Assert.NotEqual(IntPtr.Zero, pointer);
        try
        {
            var args = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))).ToArray();
            Assert.Equal(new[] { "CompactMate.exe", "--verb", "CompressZip", source, "--destination", destination }, args);
        }
        finally { LocalFree(pointer); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);

    [Fact]
    public void ParseExecutable_reads_the_quoted_open_command()
    {
        Assert.Equal(
            @"D:\Tools\CompactMate.exe",
            CompactMateHost.ParseExecutable(@"""D:\Tools\CompactMate.exe"" ""%1"""));
        Assert.Equal(
            @"C:\CompactMate.exe",
            CompactMateHost.ParseExecutable(@"C:\CompactMate.exe %1"));
        Assert.Null(CompactMateHost.ParseExecutable(" "));
    }

    [Fact]
    public void TryFind_uses_the_registered_archive_open_command()
    {
        var folder = Directory.CreateTempSubdirectory("filesmate-cm-");
        var exe = Path.Combine(folder.FullName, "CompactMate.exe");
        File.WriteAllBytes(exe, [0]);
        var registry = new MemoryUserRegistry();
        registry.SetDefaultValue(
            CompactMateHost.ArchiveOpenCommandKey,
            "\"" + exe + "\" \"%1\"");

        Assert.True(CompactMateHost.TryFind(registry, extraCandidates: [], out var found, includeNearby: false));
        Assert.Equal(Path.GetFullPath(exe), found);
    }

    [Fact]
    public void TryFind_accepts_an_explicit_candidate_when_registry_is_empty()
    {
        var folder = Directory.CreateTempSubdirectory("filesmate-cm-");
        var exe = Path.Combine(folder.FullName, "CompactMate.exe");
        File.WriteAllBytes(exe, [0]);
        var registry = new MemoryUserRegistry();

        Assert.True(CompactMateHost.TryFind(registry, [exe], out var found, includeNearby: false));
        Assert.Equal(Path.GetFullPath(exe), found);
        Assert.False(CompactMateHost.TryFind(registry, extraCandidates: [], out _, includeNearby: false));
    }

    [Fact]
    public void Create_uses_verb_and_quoted_paths()
    {
        var launch = CompactMateHost.Create(
            @"D:\CompactMate\CompactMate.exe",
            CompactMateVerb.ExtractHere,
            [@"C:\Temp\payload.zip"]);

        Assert.Equal(@"D:\CompactMate\CompactMate.exe", launch.FileName);
        Assert.Equal(
            "--verb ExtractHere C:\\Temp\\payload.zip",
            launch.Arguments);
        Assert.Equal("SmartExtract", CompactMateHost.VerbName(CompactMateVerb.SmartExtract));
        Assert.Equal("CompressZip", CompactMateHost.VerbName(CompactMateVerb.CompressZip));
    }

    [Fact]
    public void Create_writes_an_item_list_for_multiple_paths()
    {
        var launch = CompactMateHost.Create(
            @"D:\CompactMate.exe",
            CompactMateVerb.Compress7z,
            [@"C:\a.txt", @"C:\b folder\c.txt"]);

        Assert.Contains("--verb Compress7z", launch.Arguments, StringComparison.Ordinal);
        Assert.Contains("--item-list", launch.Arguments, StringComparison.Ordinal);
        var start = launch.Arguments.IndexOf("--item-list", StringComparison.Ordinal) + "--item-list".Length;
        var list = launch.Arguments[start..].Trim().Trim('"');
        Assert.True(File.Exists(list));
        var lines = File.ReadAllLines(list);
        Assert.Equal(2, lines.Length);
        Assert.Contains("a.txt", lines[0], StringComparison.Ordinal);
        Assert.Contains("c.txt", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Open_does_not_send_a_verb_switch()
    {
        var launch = CompactMateHost.Create(
            @"D:\CompactMate.exe",
            CompactMateVerb.Open,
            [@"C:\payload.7z"]);

        Assert.Equal(@"C:\payload.7z", launch.Arguments);
        Assert.DoesNotContain("--verb", launch.Arguments, StringComparison.Ordinal);
    }
}
