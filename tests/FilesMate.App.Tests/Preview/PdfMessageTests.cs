using FilesMate.App.Preview;

namespace FilesMate.App.Tests.Preview;

public sealed class PdfMessageTests
{
    [Theory]
    [InlineData(PdfPreviewAssets.ViewerUri, "\"close-preview\"", true)]
    [InlineData(PdfPreviewAssets.DocumentUri, "\"close-preview\"", false)]
    [InlineData("https://filesmate-pdf.local.evil.invalid/index.html", "\"close-preview\"", false)]
    [InlineData(PdfPreviewAssets.ViewerUri, "{}", false)]
    [InlineData(PdfPreviewAssets.ViewerUri, "null", false)]
    [InlineData(PdfPreviewAssets.ViewerUri, "[\"close-preview\"]", false)]
    [InlineData(PdfPreviewAssets.ViewerUri, "malformed", false)]
    public void Only_the_trusted_viewers_exact_string_command_is_accepted(string source, string message, bool expected)
        => Assert.Equal(expected, PdfPreviewAssets.IsCloseMessage(source, message));
}
