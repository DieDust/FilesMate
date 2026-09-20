using FilesMate.App.Icons;

namespace FilesMate.App.Tests.Icons;

public sealed class FileTypeIconCatalogTests
{
    [Theory]
    [InlineData("paper.pdf", "Pdf")]
    [InlineData("letter.docx", "Word")]
    [InlineData("budget.xlsx", "Spreadsheet")]
    [InlineData("pitch.pptx", "Presentation")]
    [InlineData("notes.txt", "Text")]
    [InlineData("README.md", "Markdown")]
    [InlineData("package.json", "Json")]
    [InlineData("layout.xml", "Xml")]
    [InlineData("index.html", "Html")]
    [InlineData("records.csv", "Csv")]
    [InlineData("service.log", "Log")]
    [InlineData("book.epub", "Ebook")]
    [InlineData("app.js", "JavaScript")]
    [InlineData("view.tsx", "TypeScript")]
    [InlineData("Program.cs", "CSharp")]
    [InlineData("native.cpp", "Cpp")]
    [InlineData("script.py", "Python")]
    [InlineData("lib.rs", "Rust")]
    [InlineData("main.go", "Go")]
    [InlineData("Main.java", "Java")]
    [InlineData("pipeline.yaml", "Yaml")]
    [InlineData("Cargo.toml", "Toml")]
    [InlineData("bundle.zip", "Zip")]
    [InlineData("bundle.7z", "SevenZip")]
    [InlineData("bundle.rar", "Rar")]
    [InlineData("bundle.tar", "Tar")]
    public void Common_formats_have_a_specific_second_level_identity(string path, string expected)
    {
        Assert.Equal(expected, FileTypeIconCatalog.ClassifyPath(path, directory: false)?.ToString());
    }

    [Theory]
    [InlineData("paper.pdf", "pdf.svg")]
    [InlineData("package.json", "json.svg")]
    [InlineData("README.md", "markdown.svg")]
    [InlineData("letter.docx", "word.svg")]
    [InlineData("budget.xlsx", "spreadsheet.svg")]
    [InlineData("pitch.pptx", "presentation.svg")]
    [InlineData("bundle.7z", "sevenzip.svg")]
    public void Specific_formats_route_to_a_dedicated_vector(string path, string asset)
    {
        var kind = FileTypeIconCatalog.ClassifyPath(path, directory: false);

        Assert.NotNull(kind);
        Assert.EndsWith('/' + asset, FileTypeIconCatalog.AssetUri(kind.Value), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("metadata.db", "Database")]
    [InlineData("cache.sqlite3", "Database")]
    [InlineData("libraryfolder.vdf", "Configuration")]
    [InlineData("settings.toml", "Toml")]
    [InlineData("driver.sys", "System")]
    [InlineData("component.dll", "System")]
    [InlineData("mystery.unknown-format", "Generic")]
    public void Common_semantic_formats_use_branded_vectors(string path, string expected)
    {
        Assert.Equal(expected, FileTypeIconCatalog.ClassifyPath(path, directory: false)?.ToString());
    }

    [Theory]
    [InlineData("application.exe")]
    [InlineData("installer.msi")]
    [InlineData("package.msix")]
    public void Identity_bearing_applications_remain_on_the_shell_path(string path)
    {
        Assert.Null(FileTypeIconCatalog.ClassifyPath(path, directory: false));
    }

    [Fact]
    public void Shortcuts_keep_the_link_kind_and_shell_preference()
    {
        var kind = FileTypeIconCatalog.ClassifyPath("application.lnk", directory: false);

        Assert.Equal(FileIconKind.Link, kind);
        Assert.True(FileTypeIconCatalog.PrefersShell(kind));
    }

    [Theory]
    [InlineData("bundle.zip")]
    [InlineData("bundle.7z")]
    [InlineData("bundle.rar")]
    [InlineData("bundle.tar")]
    [InlineData("payload.iso")]
    [InlineData("split.001")]
    public void CompactMate_extractable_files_are_archives(string path)
    {
        Assert.True(FileTypeIconCatalog.IsArchivePath(path));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Program.cs")]
    [InlineData(null)]
    [InlineData("")]
    public void Ordinary_files_are_not_archives(string? path)
    {
        Assert.False(FileTypeIconCatalog.IsArchivePath(path));
    }
}
