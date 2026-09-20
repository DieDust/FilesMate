using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;

namespace FilesMate.App.Tests.Preview;

public sealed class OfficePreviewProviderTests
{
    [Theory]
    [InlineData("会议.DOCX")]
    [InlineData("旧文档.doc")]
    [InlineData("表格.xlsx")]
    [InlineData("表格.XLS")]
    [InlineData("说明.rtf")]
    [InlineData("演示.pptx")]
    [InlineData("演示.PPT")]
    public void Supports_common_office_extensions(string path) => Assert.True(new OfficePreviewProvider().CanHandle(path));

    [Fact]
    public async Task Cancellation_before_read_does_not_launch_worker()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OfficePreviewProvider().CreateAsync(new PreviewRequest("missing.docx", 1), cancellation.Token));
    }

    [Fact]
    public async Task Large_document_returns_an_explanation_without_starting_parser()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        try
        {
            using (var file = File.Create(path)) file.SetLength(65L * 1024 * 1024);
            var result = await new OfficePreviewProvider().CreateAsync(new PreviewRequest(path, 1));
            Assert.Contains("64 MB", Assert.IsType<PreviewResult.Unsupported>(result).Reason);
        }
        finally { File.Delete(path); }
    }
}
