using System.Text;
using System.Text.Json;

namespace FilesMate.Platform.Windows.Shell;

/// <summary>Opaque Shell identities stay separate from local filesystem paths.</summary>
public sealed record PortableDeviceLocation(string Root, string RootName, PortableDeviceSegment[] Segments)
{
    public const string Prefix = "filesmate-device:";
    public const string ComputerPrefix = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}\\";
    public const string InterfaceId = "{6ac27878-a6fa-4155-ba85-f98f491d4f33}";
    public string Name => Segments.Length == 0 ? RootName : Segments[^1].Name;
    public string ParsingName => Segments.Length == 0 ? Root : Segments[^1].Path;
    public string Address => string.Join(" › ", new[] { RootName }.Concat(Segments.Select(s => s.Name)));
    public string Uri => Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, Options))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };

    public static bool IsDeviceRoot(string? path) => path is { Length: < 32768 }
        && path.StartsWith(ComputerPrefix, StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(InterfaceId, StringComparison.OrdinalIgnoreCase)
        && !path.Contains('\0');

    public static bool TryParse(string? uri, out PortableDeviceLocation location)
    {
        location = null!;
        if (uri is null || uri.Length > 65536 || !uri.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            var data = uri[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            data = data.PadRight((data.Length + 3) / 4 * 4, '=');
            var value = JsonSerializer.Deserialize<PortableDeviceLocation>(Convert.FromBase64String(data), Options);
            if (value is null || !IsDeviceRoot(value.Root) || !ValidName(value.RootName)
                || value.Segments is null || value.Segments.Length > 128) return false;
            var parent = value.Root;
            foreach (var segment in value.Segments)
            {
                if (segment is null || !ValidName(segment.Name) || !IsChild(parent, segment.Path)) return false;
                parent = segment.Path;
            }
            location = value;
            return true;
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException) { return false; }
    }

    public PortableDeviceLocation Child(string name, string path)
    {
        if (!ValidName(name) || !IsChild(ParsingName, path) || Segments.Length >= 128)
            throw new ArgumentException("Invalid portable device item.");
        return this with { Segments = [.. Segments, new(name, path)] };
    }

    public PortableDeviceLocation? Parent => Segments.Length == 0 ? null : this with { Segments = Segments[..^1] };
    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 1024 && !value.Any(char.IsControl);
    private static bool IsChild(string parent, string? path) => path is { Length: < 32768 }
        && path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase)
        && path.Length > parent.Length + 1 && !path[(parent.Length + 1)..].Contains('\\') && !path.Contains('\0');
}

public sealed record PortableDeviceSegment(string Name, string Path);
public sealed record PortableDeviceEntry(string Name, PortableDeviceLocation Location, bool IsFolder, long? Size, DateTime? Modified);
