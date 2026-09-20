using Loc = FilesMate.App.Localization.StringTable;
using System.Diagnostics;

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
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--office-preview");
            start.ArgumentList.Add(Path.GetFullPath(request.Path));
            start.ArgumentList.Add(directory);
            using var process = Process.Start(start) ?? throw new IOException(Loc.Get("Preview_StartFailed"));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var exited = process.WaitForExitAsync(deadline.Token);
                while (!exited.IsCompleted)
                {
                    await Task.WhenAny(exited, Task.Delay(200, deadline.Token)).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    if (process.HasExited) break;
                    process.Refresh();
                    if (process.PrivateMemorySize64 > 384L * 1024 * 1024)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                        return new PreviewResult.Unsupported(request.Path, Loc.Get("Preview_MemoryLimit"));
                    }
                }
                await exited.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
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
