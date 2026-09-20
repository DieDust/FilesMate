using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FilesMate.Core.Updates;

namespace FilesMate.Core.Tests.Updates;

public sealed class UpdateTests
{
    [Theory]
    [InlineData("zh-CN", "中文")]
    [InlineData("zh-TW", "中文")]
    [InlineData("ja-JP", "日本語")]
    [InlineData("en-GB", "English")]
    [InlineData("fr-FR", "English")]
    public void Signed_release_notes_follow_ui_language_with_fallback(string language, string expected)
    {
        var release = Release with { LocalizedNotes = new Dictionary<string, string>
            { ["zh-CN"] = "中文", ["en-US"] = "English", ["ja-JP"] = "日本語" } };
        var verified = UpdateManifest.Verify(Sign(release), Key.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(expected, verified.GetNotes(System.Globalization.CultureInfo.GetCultureInfo(language)));
        Assert.Equal("Test release", Release.GetNotes(System.Globalization.CultureInfo.GetCultureInfo(language)));
    }

    [Fact]
    public void Rejects_ambiguous_empty_or_oversized_translations()
    {
        foreach (var notes in new[] { new Dictionary<string, string> { ["en-US"] = "" },
            new() { ["en-US"] = new string('x', 8001) }, new() { ["en-US"] = "a", ["EN-us"] = "b" } })
            Assert.Throws<InvalidDataException>(() => UpdateManifest.Validate(Release with { LocalizedNotes = notes }));
    }
    private static readonly RSA Key = RSA.Create(2048);
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("not an executable: signed download fixture");
    private static UpdateRelease Release => new(1, "FilesMate", "1.1.42.0", "1.1.42-preview", "FilesMate-Setup-1.1.42-preview-win-x64.exe",
        Installer.Length, Convert.ToHexString(SHA256.HashData(Installer)), "Test release");
    private static byte[] Sign(UpdateRelease release)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(release, UpdateManifest.JsonOptions);
        var signature = Key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return JsonSerializer.SerializeToUtf8Bytes(new UpdateManifest.SignedEnvelope(Convert.ToBase64String(payload), Convert.ToBase64String(signature)), UpdateManifest.JsonOptions);
    }

    [Fact]
    public void Accepts_authentic_release_and_orders_versions_numerically()
    {
        var release = UpdateManifest.Verify(Sign(Release), Key.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(Release, release);
        Assert.True(release.ParsedVersion > new Version(1, 1, 9, 0));
        Assert.False(release.ParsedVersion > new Version(1, 1, 43, 0));
    }

    [Fact]
    public void Rejects_modified_payload_and_unknown_key()
    {
        var envelope = JsonSerializer.Deserialize<UpdateManifest.SignedEnvelope>(Sign(Release), UpdateManifest.JsonOptions)!;
        var tampered = envelope with { Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Release with { Version = "9.9.9.9" }, UpdateManifest.JsonOptions)) };
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(tampered, UpdateManifest.JsonOptions), Key.ExportSubjectPublicKeyInfoPem()));
        using var other = RSA.Create(2048);
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Verify(Sign(Release), other.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData("../../evil.exe")]
    [InlineData("https://other.example/evil.exe")]
    [InlineData("FilesMate-Setup-..-win-x64.exe")]
    [InlineData("FilesMate-Setup-test-win-x64.exe/extra")]
    public void Rejects_unsafe_installer_names_even_when_signed(string name) =>
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Verify(Sign(Release with { FileName = name }), Key.ExportSubjectPublicKeyInfoPem()));

    [Fact]
    public void Rejects_wrong_product_size_and_invalid_envelopes()
    {
        foreach (var release in new[] { Release with { Product = "Other" }, Release with { Size = 0 }, Release with { Size = UpdateManifest.MaximumInstallerBytes + 1 }, Release with { Version = "9.0" }, Release with { Architecture = "arm64" } })
            Assert.Throws<InvalidDataException>(() => UpdateManifest.Verify(Sign(release), Key.ExportSubjectPublicKeyInfoPem()));
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Verify(new byte[UpdateManifest.MaximumBytes + 1], Key.ExportSubjectPublicKeyInfoPem()));
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Verify("{}"u8, Key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public async Task Streams_and_verifies_installer_before_returning_executable()
    {
        using var fixture = new DownloadFixture(Installer);
        var path = await fixture.Client.DownloadAsync(Release, fixture.Folder, null, default);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(path));
        Assert.EndsWith(".exe", path);
        Assert.Single(Directory.GetFiles(fixture.Folder));
        Assert.Equal("https://updates.example/preview/" + Release.FileName, fixture.Handler.RequestedUri);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejects_corrupt_or_truncated_download_and_removes_partial(bool truncate)
    {
        var bytes = truncate ? Installer[..^1] : Installer.Select(b => (byte)(b ^ 1)).ToArray();
        using var fixture = new DownloadFixture(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.DownloadAsync(Release, fixture.Folder, null, default));
        Assert.Empty(Directory.GetFiles(fixture.Folder));
    }

    [Fact]
    public async Task Cancellation_and_http_failure_never_leave_an_executable()
    {
        using var fixture = new DownloadFixture(Installer);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.DownloadAsync(Release, fixture.Folder, null, cancellation.Token));
        Assert.Empty(Directory.GetFiles(fixture.Folder));
        fixture.Handler.Status = HttpStatusCode.NotFound;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Client.DownloadAsync(Release, fixture.Folder, null, default));
        Assert.Empty(Directory.GetFiles(fixture.Folder));
    }

    [Fact]
    public async Task Fetches_signed_feed_and_rejects_insecure_endpoint()
    {
        using var fixture = new DownloadFixture(Sign(Release));
        Assert.Equal(Release, await fixture.Client.CheckAsync(default));
        var insecure = new UpdateClient(fixture.Http, new Uri("http://updates.example/"), Key.ExportSubjectPublicKeyInfoPem());
        await Assert.ThrowsAsync<InvalidOperationException>(() => insecure.CheckAsync(default));
    }

    private sealed class Handler(byte[] content) : HttpMessageHandler
    {
        public string? RequestedUri { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(content) });
        }
    }
    private sealed class DownloadFixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "FilesMate-update-test-" + Guid.NewGuid().ToString("N"));
        public Handler Handler { get; }
        public HttpClient Http { get; }
        public UpdateClient Client { get; }
        public DownloadFixture(byte[] content)
        {
            Directory.CreateDirectory(Folder); Handler = new(content); Http = new(Handler);
            Client = new(Http, new Uri("https://updates.example/preview/"), Key.ExportSubjectPublicKeyInfoPem());
        }
        public void Dispose() { Http.Dispose(); Directory.Delete(Folder, recursive: true); }
    }
}
