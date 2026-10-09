using System.Text.Json;
using FilesMate.Search;

namespace FilesMate.Search.Tests;

public sealed class SearchHostDispatchEnvelopeTests
{
    [Theory]
    [InlineData("status", "appearance-preview")]
    [InlineData("apply", "appearance-preview")]
    [InlineData("appearance-preview", "status")]
    [InlineData("appearance-preview", "apply")]
    public void DuplicateCommandCannotBypassStrictDispatch(string first, string last)
    {
        var json = "{\"Command\":" + JsonSerializer.Serialize(first) + ",\"Command\":"
            + JsonSerializer.Serialize(last) + ",\"Settings\":{},\"ManagerPath\":\"FilesMate.App.exe\"}";
        Assert.False(SearchHostProtocol.TryReadRequest(json, out var request));
        Assert.Null(request);
    }

    [Fact]
    public void OtherDuplicateRootKeysAreAlsoRejectedBeforeDispatch()
    {
        Assert.False(SearchHostProtocol.TryReadRequest("{\"Command\":\"apply\",\"Settings\":null,\"Settings\":{}}", out _));
    }

    [Fact]
    public void ValidGenericStatusStillRoundTrips()
    {
        var request = new SearchHostRequest("status");
        Assert.True(SearchHostProtocol.TryReadRequest(JsonSerializer.Serialize(request), out var parsed));
        Assert.Equal(request, parsed);
    }

    [Fact]
    public void AppearanceDispatchUsesStrictEnvelopeValidator()
    {
        var invalid = new SearchHostRequest("appearance-preview");
        Assert.False(SearchHostProtocol.TryReadRequest(JsonSerializer.Serialize(invalid), out _));
    }
}
