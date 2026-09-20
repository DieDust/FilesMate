using System.Buffers;
using System.Security.Cryptography;

namespace FilesMate.Core.Updates;

public sealed class UpdateClient(HttpClient http, Uri feedDirectory, string publicKeyPem)
{
    public async Task<UpdateRelease> CheckAsync(CancellationToken cancellationToken)
    {
        ValidateEndpoint();
        using var response = await http.GetAsync(new Uri(feedDirectory, "latest.json"), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > UpdateManifest.MaximumBytes) throw new InvalidDataException("Update manifest is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > UpdateManifest.MaximumBytes) throw new InvalidDataException("Update manifest is too large.");
            output.Write(buffer, 0, read);
        }
        return UpdateManifest.Verify(output.ToArray(), publicKeyPem);
    }

    public async Task<string> DownloadAsync(UpdateRelease release, string cacheDirectory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ValidateEndpoint();
        UpdateManifest.Validate(release);
        Directory.CreateDirectory(cacheDirectory);
        var partial = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N") + ".part");
        var complete = Path.ChangeExtension(partial, ".exe");
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            using var response = await http.GetAsync(new Uri(feedDirectory, release.FileName), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new InvalidDataException("Installer length mismatch.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            var lastProgress = Environment.TickCount64;
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("Installer exceeds signed size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (Environment.TickCount64 - lastProgress >= 100)
                    {
                        progress?.Report((double)total / release.Size);
                        lastProgress = Environment.TickCount64;
                    }
                }
                if (total != release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                    throw new InvalidDataException("Installer integrity check failed.");
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, complete);
            progress?.Report(1);
            return complete;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            try { File.Delete(partial); } catch (IOException) { }
        }
    }

    private void ValidateEndpoint()
    {
        if (!feedDirectory.IsAbsoluteUri || feedDirectory.Scheme != Uri.UriSchemeHttps || !feedDirectory.AbsolutePath.EndsWith('/'))
            throw new InvalidOperationException("Updates require an HTTPS feed directory.");
    }
}
