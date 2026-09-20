using FilesMate.App.Preview;

namespace FilesMate.App.Tests.Preview;

public sealed class OfficePreviewHandlerPolicyTests
{
    [Theory]
    [InlineData("WPS.Docx.6", null, "Wps")]
    [InlineData("ET.Xlsx.6", null, "Wps")]
    [InlineData("WPP.PPTX.6", null, "Wps")]
    [InlineData("Word.Document.12", null, "MicrosoftOffice")]
    [InlineData("Excel.Sheet.12", null, "MicrosoftOffice")]
    [InlineData("PowerPoint.Show.12", null, "MicrosoftOffice")]
    [InlineData(null, "C:\\Office\\WINWORD.EXE", "MicrosoftOffice")]
    [InlineData("Applications\\wps.exe", "C:\\WPS\\wps.exe", "Wps")]
    [InlineData("Other.Editor", "C:\\Tools\\excel-helper.exe", "Unknown")]
    [InlineData(null, null, "Unknown")]
    public void Recognizes_default_application_without_guessing_from_document_name(string? progId, string? executable, string expected)
        => Assert.Equal(expected, OfficePreviewHandlerPolicy.DetectApplication(progId, executable).ToString());

    [Fact]
    public void Office_default_wins_even_when_Wps_owns_shell_preview_association()
    {
        var wps = new OfficePreviewHandlerCandidate(Guid.NewGuid(), "WPS表格 预览器");
        var office = new OfficePreviewHandlerCandidate(Guid.NewGuid(), "Microsoft Excel previewer");
        Assert.Equal(new[] { office.ClassId, wps.ClassId }, OfficePreviewHandlerPolicy.Order(OfficePreviewFamily.MicrosoftOffice, [wps, office], wps.ClassId, wps.ClassId));
    }

    [Fact]
    public void Wps_default_wins_even_when_Office_was_registered_first()
    {
        var office = new OfficePreviewHandlerCandidate(Guid.NewGuid(), "Microsoft Word previewer");
        var wps = new OfficePreviewHandlerCandidate(Guid.NewGuid(), "WPS文字 预览器");
        Assert.Equal(new[] { wps.ClassId, office.ClassId }, OfficePreviewHandlerPolicy.Order(OfficePreviewFamily.Wps, [office, wps], office.ClassId, office.ClassId));
    }

    [Fact]
    public void Missing_preferred_provider_keeps_another_installed_provider_as_fallback()
    {
        var office = new OfficePreviewHandlerCandidate(Guid.NewGuid(), "Microsoft PowerPoint previewer");
        Assert.Equal(new[] { office.ClassId }, OfficePreviewHandlerPolicy.Order(OfficePreviewFamily.Wps, [office], null, null));
        Assert.Empty(OfficePreviewHandlerPolicy.Order(OfficePreviewFamily.Wps, [], null, null));
    }

    [Fact]
    public void Unknown_default_respects_association_and_deduplicates_registry_views()
    {
        var generic = new OfficePreviewHandlerCandidate(Guid.NewGuid(), "Third party previewer");
        var office = new OfficePreviewHandlerCandidate(Guid.NewGuid(), "Microsoft Word previewer");
        Assert.Equal(new[] { generic.ClassId, office.ClassId }, OfficePreviewHandlerPolicy.Order(OfficePreviewFamily.Unknown, [office, generic, office], generic.ClassId, office.ClassId));
    }
}
