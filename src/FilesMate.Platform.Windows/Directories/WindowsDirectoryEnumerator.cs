using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Errors;
using FilesMate.Platform.Windows.Interop;
using FilesMate.Platform.Windows.Paths;

namespace FilesMate.Platform.Windows.Directories;

public sealed class WindowsDirectoryEnumerator : IDirectoryEnumerator
{
    private readonly WindowsPathNormalizer _paths;

    public WindowsDirectoryEnumerator()
        : this(new WindowsPathNormalizer())
    {
    }

    public WindowsDirectoryEnumerator(WindowsPathNormalizer paths)
    {
        _paths = paths;
    }

    public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(
        DirectoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<DirectoryBatch>(new BoundedChannelOptions(4)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var worker = Task.Run(() => ProduceAsync(request, channel.Writer, cancellationToken), CancellationToken.None);

        try
        {
            await foreach (var batch in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return batch;
            }
        }
        finally
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task ProduceAsync(
        DirectoryRequest request,
        ChannelWriter<DirectoryBatch> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var batch in EnumerateBlocking(request, cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteAsync(batch, cancellationToken).ConfigureAwait(false);
            }

            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private async IAsyncEnumerable<DirectoryBatch> EnumerateBlocking(
        DirectoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        string normalized;
        DirectoryReadError? pathError = null;
        try
        {
            normalized = _paths.Normalize(request.Path);
        }
        catch (ArgumentException ex)
        {
            normalized = request.Path;
            pathError = new DirectoryReadError(DirectoryReadErrorKind.PathInvalid, 0, ex.Message, isTerminal: true);
        }

        if (pathError is not null)
        {
            yield return DirectoryBatch.Create(
                request.PaneId,
                request.Generation,
                request.Path,
                [],
                isFinal: true,
                pathError);
            yield break;
        }

        var searchPath = normalized.EndsWith('\\') ? normalized + "*" : normalized + "\\*";
        var buffer = new List<FileEntryCore>(request.Options.BatchSize);
        var clock = Stopwatch.StartNew();
        var nextId = 1;

        SafeFindHandle? handle = null;
        WIN32_FIND_DATAW data;
        try
        {
            handle = OpenFind(searchPath, out data);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                yield return DirectoryBatch.Create(
                    request.PaneId,
                    request.Generation,
                    normalized,
                    [],
                    isFinal: true,
                    Win32ErrorMapper.Map(error));
                yield break;
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DirectoryEntryConverter.TryConvert(in data, request.Options, nextId, out var entry))
                {
                    buffer.Add(entry);
                    nextId++;
                }

                if (ShouldFlush(buffer, request.Options, clock))
                {
                    yield return DirectoryBatch.Create(
                        request.PaneId,
                        request.Generation,
                        normalized,
                        buffer.ToArray(),
                        isFinal: false,
                        error: null);
                    buffer.Clear();
                    clock.Restart();
                }

                if (!Kernel32.FindNextFileW(handle, out data))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error is Kernel32.ErrorNoMoreFiles or Kernel32.ErrorFileNotFound)
                    {
                        break;
                    }

                    yield return DirectoryBatch.Create(
                        request.PaneId,
                        request.Generation,
                        normalized,
                        buffer.ToArray(),
                        isFinal: true,
                        Win32ErrorMapper.Map(error, isTerminal: false));
                    yield break;
                }
            }
        }
        finally
        {
            handle?.Dispose();
        }

        yield return DirectoryBatch.Create(
            request.PaneId,
            request.Generation,
            normalized,
            buffer.ToArray(),
            isFinal: true,
            error: null);
    }

    private static bool ShouldFlush(List<FileEntryCore> buffer, DirectoryReadOptions options, Stopwatch clock) =>
        buffer.Count >= options.BatchSize ||
        (buffer.Count > 0 && clock.Elapsed >= options.BatchDeadline);

    private static SafeFindHandle OpenFind(string searchPath, out WIN32_FIND_DATAW data)
    {
        var handle = Kernel32.FindFirstFileExW(
            searchPath,
            FINDEX_INFO_LEVELS.FindExInfoBasic,
            out data,
            FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            nint.Zero,
            Kernel32.FindFirstExLargeFetch);

        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != Kernel32.ErrorInvalidParameter)
        {
            return handle;
        }

        handle.Dispose();
        return Kernel32.FindFirstFileExW(
            searchPath,
            FINDEX_INFO_LEVELS.FindExInfoBasic,
            out data,
            FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            nint.Zero,
            0);
    }
}
