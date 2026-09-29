using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;
using FilesMate.Core.Operations;

namespace FilesMate.App.Services;

public sealed record FileTextComparison(TextDifferenceResult Difference, bool IsPartial, bool? SameBytes);

public static class FileContentComparison
{
    public const int TextByteLimit = 256 * 1024;

    public static async Task<FileTextComparison?> ReadTextAsync(string incoming, string existing, CancellationToken token = default)
    {
        var provider = new TextPreviewProvider();
        if (!provider.CanHandle(incoming) || !provider.CanHandle(existing)) return null;
        // Hold both names while reading. A comparison must not quietly combine two versions.
        using var leftGuard = Open(incoming);
        using var rightGuard = Open(existing);
        var left = await provider.CreateAsync(new(incoming, 1, TextByteLimit), token).ConfigureAwait(false);
        var right = await provider.CreateAsync(new(existing, 1, TextByteLimit), token).ConfigureAwait(false);
        if (left is not PreviewResult.Text a || right is not PreviewResult.Text b) return null;
        var difference = await Task.Run(() => TextFileDifference.Compare(a.Content, b.Content, token), token).ConfigureAwait(false);
        var partial = a.IsTruncated || b.IsTruncated || difference.IsLimited
            || a.Content.Contains('\uFFFD') || b.Content.Contains('\uFFFD');
        // Equal displayed text is not proof of equal files: encodings and line endings can differ.
        bool? same = partial ? null : await EqualStreamsAsync(leftGuard, rightGuard, token).ConfigureAwait(false);
        return new(difference, partial, same);
    }

    public static async Task<bool> EqualBytesAsync(string incoming, string existing, CancellationToken token = default)
    {
        using var left = Open(incoming);
        using var right = Open(existing);
        return await EqualStreamsAsync(left, right, token).ConfigureAwait(false);
    }

    private static FileStream Open(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("Only regular files can be compared.");
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static async Task<bool> EqualStreamsAsync(FileStream left, FileStream right, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (left.Length != right.Length) return false;
        var a = new byte[64 * 1024]; var b = new byte[a.Length];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await left.ReadAtLeastAsync(a, a.Length, throwOnEndOfStream: false, token).ConfigureAwait(false);
            var other = await right.ReadAtLeastAsync(b, b.Length, throwOnEndOfStream: false, token).ConfigureAwait(false);
            if (read != other || !a.AsSpan(0, read).SequenceEqual(b.AsSpan(0, other))) return false;
            if (read == 0) return true;
        }
    }
}
