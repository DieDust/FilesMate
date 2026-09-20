using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FilesMate.Core.Updates;

public sealed record UpdateRelease(int Schema, string Product, string Version, string DisplayVersion,
    string FileName, long Size, string Sha256, string Notes, string Architecture = "win-x64", int MinimumWindowsBuild = 22621,
    IReadOnlyDictionary<string, string>? LocalizedNotes = null)
{
    public System.Version ParsedVersion => System.Version.Parse(Version);

    public string GetNotes(System.Globalization.CultureInfo culture)
    {
        if (LocalizedNotes is not { Count: > 0 }) return Notes;
        string? Find(string language) => LocalizedNotes.FirstOrDefault(p =>
            string.Equals(p.Key, language, StringComparison.OrdinalIgnoreCase)).Value;
        for (var candidate = culture; !string.IsNullOrEmpty(candidate.Name); candidate = candidate.Parent)
            if (Find(candidate.Name) is { Length: > 0 } exact) return exact;
        var family = culture.TwoLetterISOLanguageName switch { "zh" => "zh-CN", "ja" => "ja-JP", "en" => "en-US", _ => "en-US" };
        return Find(family) ?? Find("en-US") ?? Find("en") ?? Notes;
    }
}

public static partial class UpdateManifest
{
    public const int MaximumBytes = 64 * 1024;
    public const long MaximumInstallerBytes = 512L * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static UpdateRelease Verify(ReadOnlySpan<byte> envelope, string publicKeyPem)
    {
        if (envelope.Length > MaximumBytes) throw new InvalidDataException("Update manifest is too large.");
        try
        {
            var signed = JsonSerializer.Deserialize<SignedEnvelope>(envelope, JsonOptions)
                ?? throw new InvalidDataException("Missing update manifest.");
            var payload = Convert.FromBase64String(signed.Payload);
            var signature = Convert.FromBase64String(signed.Signature);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new InvalidDataException("Update signature is invalid.");
            var release = JsonSerializer.Deserialize<UpdateRelease>(payload, JsonOptions)
                ?? throw new InvalidDataException("Missing release data.");
            Validate(release);
            return release;
        }
        catch (Exception error) when (error is JsonException or FormatException or CryptographicException or ArgumentException)
        { throw new InvalidDataException("Invalid signed update manifest.", error); }
    }

    public static void Validate(UpdateRelease release)
    {
        if (release.Schema != 1 || release.Product != "FilesMate" || release.Architecture != "win-x64"
            || !VersionPattern().IsMatch(release.Version ?? "") || !System.Version.TryParse(release.Version, out _)
            || release.FileName is null || !FileNamePattern().IsMatch(release.FileName) || release.FileName.Contains("..", StringComparison.Ordinal)
            || release.Size <= 0 || release.Size > MaximumInstallerBytes || !HashPattern().IsMatch(release.Sha256 ?? "")
            || string.IsNullOrWhiteSpace(release.DisplayVersion) || release.DisplayVersion.Length > 100
            || release.Notes is null || release.Notes.Length > 8000 || release.MinimumWindowsBuild < 22621)
            throw new InvalidDataException("Unsupported update release.");
        if (release.LocalizedNotes is { } notes && (notes.Count > 16
            || notes.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != notes.Count || notes.Any(p =>
            !Regex.IsMatch(p.Key, @"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$")
            || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 8000)))
            throw new InvalidDataException("Invalid localized release notes.");
    }

    [GeneratedRegex(@"^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$")]
    private static partial Regex VersionPattern();
    [GeneratedRegex(@"^FilesMate-Setup-[A-Za-z0-9.-]{1,140}-win-x64\.exe$")]
    private static partial Regex FileNamePattern();
    [GeneratedRegex(@"^[A-Fa-f0-9]{64}$")]
    private static partial Regex HashPattern();
    public sealed record SignedEnvelope(string Payload, string Signature);
}
