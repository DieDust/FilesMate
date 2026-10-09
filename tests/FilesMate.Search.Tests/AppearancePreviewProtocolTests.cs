using System.Text.Json;
using System.Text.Json.Nodes;
using FilesMate.Search;

namespace FilesMate.Search.Tests;

public sealed class AppearancePreviewProtocolTests
{
    private static SearchAppearancePreview Preview => new("Dark", "Mica", "System", "Balanced", "Default", null, 20, null);
    private static SearchHostRequest Request => new("appearance-preview", AppearancePreview: Preview, PreviewOwnerPid: 123,
        PreviewSessionId: Guid.NewGuid().ToString("N"), PreviewEpoch: 1, PreviewOwnerStartedUtcTicks: 456,
        PreviewOperation: AppearancePreviewProtocol.Update);

    [Theory]
    [InlineData(AppearancePreviewProtocol.Acquire)]
    [InlineData(AppearancePreviewProtocol.Update)]
    [InlineData(AppearancePreviewProtocol.Clear)]
    public void ExactEnvelopeRoundTrips(string operation)
    {
        var request = Request with { PreviewOperation = operation,
            AppearancePreview = operation == AppearancePreviewProtocol.Update ? Preview : null };
        Assert.True(AppearancePreviewProtocol.TryRead(JsonSerializer.Serialize(request), out var parsed));
        Assert.Equal(request, parsed);
    }

    [Theory]
    [InlineData("Command")]
    [InlineData("AppearancePreview")]
    [InlineData("PreviewOwnerPid")]
    [InlineData("PreviewSessionId")]
    [InlineData("PreviewEpoch")]
    [InlineData("PreviewOwnerStartedUtcTicks")]
    [InlineData("PreviewOperation")]
    public void RequiredFieldsCannotBeOmitted(string field)
    {
        var node = JsonSerializer.SerializeToNode(Request)!.AsObject();
        node.Remove(field);
        Assert.False(AppearancePreviewProtocol.TryRead(node.ToJsonString(), out _));
    }

    [Theory]
    [InlineData("Unknown", "true")]
    [InlineData("Command", "12")]
    [InlineData("PreviewOwnerPid", "\"123\"")]
    [InlineData("PreviewOwnerPid", "0")]
    [InlineData("PreviewEpoch", "0")]
    [InlineData("PreviewEpoch", "1.5")]
    [InlineData("PreviewOwnerStartedUtcTicks", "9223372036854775807")]
    [InlineData("PreviewSessionId", "\"00000000000000000000000000000000\"")]
    [InlineData("PreviewOperation", "\"Clear\"")]
    [InlineData("PreviewOperation", "\"clear\"")]
    [InlineData("AppearancePreview", "null")]
    [InlineData("ManagerPath", "\"C:/Other.exe\"")]
    [InlineData("Settings", "{}")]
    public void WrongTypesBoundsAndUnexpectedFieldsAreRejected(string field, string value)
    {
        var node = JsonSerializer.SerializeToNode(Request)!.AsObject();
        node[field] = JsonNode.Parse(value);
        Assert.False(AppearancePreviewProtocol.TryRead(node.ToJsonString(), out _));
    }

    [Theory]
    [InlineData("Theme")]
    [InlineData("Backdrop")]
    [InlineData("ReduceMotion")]
    [InlineData("GlassEffect")]
    [InlineData("Accent")]
    [InlineData("CustomAccent")]
    [InlineData("TransparencyPercent")]
    [InlineData("SkinJson")]
    public void PreviewFieldsCannotBeOmitted(string field)
    {
        var node = JsonSerializer.SerializeToNode(Request)!.AsObject();
        node["AppearancePreview"]!.AsObject().Remove(field);
        Assert.False(AppearancePreviewProtocol.TryRead(node.ToJsonString(), out _));
    }

    [Fact]
    public void DuplicateKeysAreRejectedAtBothLevels()
    {
        var json = JsonSerializer.Serialize(Request);
        Assert.False(AppearancePreviewProtocol.TryRead(json.Insert(1, "\"PreviewEpoch\":1,"), out _));
        var inner = json.Replace("\"Theme\":", "\"Theme\":\"Light\",\"Theme\":", StringComparison.Ordinal);
        Assert.False(AppearancePreviewProtocol.TryRead(inner, out _));
    }

    [Fact]
    public void PayloadLimitsAndMalformedJsonAreRejected()
    {
        foreach (var message in new[] { "", "[]", "null", "{", new string(' ', SearchHostProtocol.MaximumRequestCharacters + 1) })
            Assert.False(AppearancePreviewProtocol.TryRead(message, out _));
        var oversized = Request with { AppearancePreview = Preview with { SkinJson = new string('x', 3001) } };
        Assert.False(AppearancePreviewProtocol.TryRead(JsonSerializer.Serialize(oversized), out _));
    }
}
