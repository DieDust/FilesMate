using FilesMate.App.Localization;
using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;

namespace FilesMate.App.Tests.Preview;

public sealed class PreviewServiceTests
{
    [Fact]
    public async Task Providers_route_common_file_types_without_ui_dependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), "filesmate-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var text = Path.Combine(root, "readme.md");
        await File.WriteAllTextAsync(text, "hello");
        var image = Path.Combine(root, "photo.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3]);

        await using var service = new PreviewService(
        [
            new ImagePreviewProvider(),
            new TextPreviewProvider(),
            new PdfPreviewProvider(),
            new MediaPreviewProvider(),
            new PropertiesPreviewProvider(),
        ]);

        var textResult = await service.LoadAsync(new PreviewRequest(text, 1));
        var imageResult = await service.LoadAsync(new PreviewRequest(image, 2));

        Assert.IsType<PreviewResult.Text>(textResult);
        Assert.Equal("hello", ((PreviewResult.Text)textResult).Content);
        Assert.IsType<PreviewResult.Image>(imageResult);
        Assert.True(new ImagePreviewProvider().CanHandle("photo.heic"));
    }

    [Fact]
    public async Task Folder_preview_reports_directory_metadata_without_enumerating_size()
    {
        var root = Path.Combine(Path.GetTempPath(), "filesmate-preview-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        var result = (PreviewResult.Properties)await new PropertiesPreviewProvider()
            .CreateAsync(new PreviewRequest(root, 1));

        Assert.True(result.IsDirectory);
        Assert.Equal(2, result.ChildCount);
        Assert.NotNull(result.CreationTimeUtc);
        Assert.Equal(Path.GetFileName(root), PreviewDetails.DisplayName(root));
        Assert.Equal(StringTable.Get("Type_Folder"), PreviewDetails.TypeLabel(root, true));
    }

    [Fact]
    public async Task Text_preview_is_capped_at_one_mib_and_marks_truncation()
    {
        var path = Path.Combine(Path.GetTempPath(), "filesmate-large-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, new string('x', 1_048_700));
        await using var service = new PreviewService([new TextPreviewProvider()]);

        var result = (PreviewResult.Text)await service.LoadAsync(new PreviewRequest(path, 1));

        Assert.Equal(1_048_576, result.Content.Length);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task A_new_generation_cancels_the_previous_preview()
    {
        var provider = new BlockingProvider();
        await using var service = new PreviewService([provider]);
        var first = service.LoadAsync(new PreviewRequest("first.bin", 1));
        await provider.Started.Task;
        var second = service.LoadAsync(new PreviewRequest("second.bin", 2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        provider.Release();
        var result = await second;
        Assert.Equal("second.bin", result.Path);
    }

    [Fact]
    public async Task Dispose_cancels_an_active_preview_without_disposing_its_token_source_early()
    {
        var provider = new CancellationInspectionProvider();
        var service = new PreviewService([provider]);
        var load = service.LoadAsync(new PreviewRequest("active.bin", 1));
        await provider.Started.Task;

        await service.DisposeAsync();
        var failure = await Record.ExceptionAsync(() => load);

        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.True(provider.RegisterAfterCancellationSucceeded);
    }

    private sealed class BlockingProvider : IPreviewProvider
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started => _started;

        public bool CanHandle(string path) => true;

        public async Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new PreviewResult.Properties(request.Path, 0, null, 0);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class CancellationInspectionProvider : IPreviewProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool RegisterAfterCancellationSucceeded { get; private set; }

        public bool CanHandle(string path) => true;

        public async Task<PreviewResult> CreateAsync(
            PreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                using var registration = cancellationToken.Register(static () => { });
                RegisterAfterCancellationSucceeded = true;
                throw;
            }

            throw new InvalidOperationException("The preview cancellation test unexpectedly continued.");
        }
    }
}
