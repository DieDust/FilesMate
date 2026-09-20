using System.IO.Pipes;
using System.Text.Json;
using FilesMate.Search;

namespace FilesMate.App.Tests.Search;

public sealed class SearchHostClientTests
{
    [Fact]
    public async Task Oversized_pipe_reply_is_rejected_instead_of_being_loaded_as_status()
    {
        var reply = await ExchangeAsync(new SearchHostReply(true, new string('x', 10000)));
        Assert.Null(reply);
    }

    [Fact]
    public async Task Normal_unicode_reply_round_trips_through_a_real_pipe()
    {
        var expected = new SearchHostReply(true, "搜索已就绪 · 検索の準備完了", true, false, 123);
        Assert.Equal(expected, await ExchangeAsync(expected));
    }

    private static async Task<SearchHostReply?> ExchangeAsync(SearchHostReply reply)
    {
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-pipe-test-" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new NamedPipeServerStream(GlobalSearchConfiguration.PipeName(profile),
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var listening = server.WaitForConnectionAsync(timeout.Token);
        var client = SearchHostClient.SendAsync(new("status"), profile, timeout.Token);
        await listening;
        using var reader = new StreamReader(server, leaveOpen: true);
        Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
        try { await server.WriteAsync(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply) + "\n"), timeout.Token); }
        catch (IOException) { /* A bounded client may close before a large reply finishes. */ }
        return await client;
    }
}
