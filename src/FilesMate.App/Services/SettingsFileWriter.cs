namespace FilesMate.App.Services;

internal static class SettingsFileWriter
{
    internal static async Task WriteAsync(string path, string json, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
                    else File.Move(temporary, path);
                    return;
                }
                catch (IOException) when (attempt < 6 && File.Exists(temporary))
                {
                    // Indexers can hold either file briefly. Keep the old complete JSON
                    // until an atomic replacement succeeds, with a bounded async retry.
                    await Task.Delay(Math.Min(250, 50 << attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            gate.Release();
        }
    }
}
