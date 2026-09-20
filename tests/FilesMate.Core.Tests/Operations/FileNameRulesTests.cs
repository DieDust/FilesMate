using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class FileNameRulesTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("a:b.txt")]
    [InlineData("NUL.txt")]
    [InlineData("com1")]
    [InlineData("LPT².log")]
    [InlineData("ends.")]
    [InlineData("ends ")]
    [InlineData("")]
    public void RejectsUnsafeWindowsNames(string name) => Assert.Throws<IOException>(() => FileNameRules.Validate(name));

    [Theory]
    [InlineData("作业 2026.txt", false, 7)]
    [InlineData("archive.tar.gz", false, 11)]
    [InlineData(".gitignore", false, 10)]
    [InlineData("folder.name", true, 11)]
    public void RenameSelectsStemAndPreservesExtension(string name, bool directory, int length)
    {
        Assert.Equal(name, FileNameRules.Validate(name));
        Assert.Equal(length, FileNameRules.StemLength(name, directory));
    }
}
