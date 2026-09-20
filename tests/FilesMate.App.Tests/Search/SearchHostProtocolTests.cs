using FilesMate.Search;

namespace FilesMate.App.Tests.Search;

public sealed class SearchHostProtocolTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Exact_limit_and_terminators_across_buffer_boundaries_are_accepted(string terminator)
    {
        var message = new string('日', 255);
        using var reader = new StringReader(message + terminator);
        Assert.Equal(message, await SearchHostProtocol.ReadAsync(reader, 255, default));
    }

    [Theory]
    [InlineData("123456\n")]
    [InlineData("123456")]
    [InlineData("123456\r\n")]
    public async Task Oversize_messages_are_rejected_with_or_without_a_newline(string message)
    {
        using var reader = new StringReader(message);
        await Assert.ThrowsAsync<InvalidDataException>(() => SearchHostProtocol.ReadAsync(reader, 5, default));
    }

    [Fact]
    public async Task Truncated_message_is_not_executed_as_a_complete_command()
    {
        using var reader = new StringReader("{\"Command\":\"stop\"}");
        await Assert.ThrowsAsync<EndOfStreamException>(() => SearchHostProtocol.ReadAsync(reader, 4096, default));
    }

    [Fact]
    public async Task Clean_disconnect_and_cancellation_are_handled()
    {
        using var reader = new StringReader("");
        Assert.Null(await SearchHostProtocol.ReadAsync(reader, 4096, default));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SearchHostProtocol.ReadAsync(reader, 4096, canceled.Token));
    }
}
