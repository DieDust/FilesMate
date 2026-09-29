using System.IO.Compression;
using System.Xml.Linq;
using FilesMate.App.Commands;
using FilesMate.Core.Operations;

namespace FilesMate.App.Tests.Operations;

public sealed class NewDocumentTemplateTests
{
    [Theory]
    [InlineData(NewDocumentKind.Word, "word/document.xml", "document")]
    [InlineData(NewDocumentKind.Spreadsheet, "xl/workbook.xml", "workbook")]
    [InlineData(NewDocumentKind.Presentation, "ppt/presentation.xml", "presentation")]
    public void Office_templates_have_a_valid_package_graph_and_xml(NewDocumentKind kind, string main, string root)
    {
        using var bytes = new MemoryStream(NewDocumentTemplate.Content(kind));
        using var zip = new ZipArchive(bytes);
        Assert.True(bytes.Length > 0);
        foreach (var part in zip.Entries)
        {
            using var stream = part.Open();
            var xml = XDocument.Load(stream);
            Assert.NotNull(xml.Root);
            if (part.FullName == main) Assert.Equal(root, xml.Root.Name.LocalName);
            if (part.FullName.EndsWith(".rels"))
            {
                var origin = part.FullName == "_rels/.rels" ? new Uri("https://package/")
                    : new Uri("https://package/" + part.FullName.Replace("_rels/", "")[..^5]);
                foreach (var relationship in xml.Root.Elements())
                    Assert.NotNull(zip.GetEntry(new Uri(origin, relationship.Attribute("Target")!.Value).AbsolutePath.TrimStart('/')));
            }
        }
        using var rootStream = zip.GetEntry("_rels/.rels")!.Open();
        Assert.Equal(main, XDocument.Load(rootStream).Root!.Elements().Single().Attribute("Target")!.Value);
    }
    [Fact]
    public void Spreadsheet_is_readable_by_an_independent_workbook_reader()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var bytes = new MemoryStream(NewDocumentTemplate.Content(NewDocumentKind.Spreadsheet));
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateOpenXmlReader(bytes);
        Assert.Equal("Sheet1", reader.Name);
        Assert.False(reader.Read());
    }
    [Fact]
    public void Creation_commands_respect_folder_and_portable_device_restrictions()
    {
        foreach (var command in NewDocumentCommands.All)
        {
            var readOnly = CommandContext.SingleFile with { IsBackground = true, IsFolderWritable = false };
            Assert.False(CommandCatalog.CanExecute(command, readOnly));
            Assert.False(CommandCatalog.CanExecute(command, readOnly with { IsFolderWritable = true, IsPortableDevice = true }));
            Assert.True(CommandCatalog.CanExecute(command, readOnly with { IsFolderWritable = true }));
        }
        Assert.Equal(9, NewDocumentCommands.All.Select(id => NewDocumentTemplate.Extension(NewDocumentCommands.Kind(id))).Distinct().Count());
    }
}
