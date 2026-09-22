using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Platform.Windows.Processes;

namespace FilesMate.App.Preview.Providers;

/// <summary>Office parsers run in a disposable process, never on the UI thread.</summary>
public sealed class OfficePreviewProvider : IPreviewProvider
{
    private static readonly SemaphoreSlim Gate = new(1);
    public bool CanHandle(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".docx" or ".doc" or ".rtf" or ".xlsx" or ".xls" or ".pptx" or ".ppt";

    public async Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-OfficePreview", Guid.NewGuid().ToString("N"));
        try
        {
            var info = new FileInfo(request.Path);
            if (info.Length > 64 * 1024 * 1024)
                return new PreviewResult.Unsupported(request.Path, Loc.Get("Preview_DocumentTooLarge"));
            Directory.CreateDirectory(directory);
            var executable = Path.Combine(AppContext.BaseDirectory, "SearchHost", "FilesMate.SearchHost.exe");
            if (!File.Exists(executable)) executable = Path.Combine(AppContext.BaseDirectory, "FilesMate.SearchHost.exe");
            using var worker = PreviewWorkerProcess.Start(executable, ["--office-preview", Path.GetFullPath(request.Path), directory]);
            var process = worker.Process;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                // The job enforces the memory ceiling before the converter starts.
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                worker.Terminate();
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new PreviewResult.Unsupported(request.Path, Loc.Get("Preview_Timeout"));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var output = Path.Combine(directory, "preview.html");
            if (process.ExitCode != 0 || !File.Exists(output))
                return new PreviewResult.Unsupported(request.Path, Loc.Get("Preview_InvalidDocument"));
            if (new FileInfo(output).Length > 24 * 1024 * 1024)
                return new PreviewResult.Unsupported(request.Path, Loc.Get("Preview_ContentTooLarge"));
            return new PreviewResult.Html(request.Path, await File.ReadAllTextAsync(output, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Gate.Release();
        }
    }
}
