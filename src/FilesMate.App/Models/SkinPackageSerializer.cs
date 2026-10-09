using System.Text;
using System.Text.Json;

namespace FilesMate.App.Models;

/// <summary>
/// Schema 1 accepts only the documented camelCase fields and opaque #RRGGBB values.
/// schemaVersion is required; missing names, groups and tokens use Warm White defaults.
/// Unknown fields and versions are rejected rather than silently losing information.
/// </summary>
public static class SkinPackageSerializer
{
    public const int MaximumUtf8Bytes = 64 * 1024;
    public const int CurrentSchemaVersion = SkinPackage.CurrentSchemaVersion;

    public static SkinPackage Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumUtf8Bytes || Encoding.UTF8.GetByteCount(json) > MaximumUtf8Bytes)
            throw new JsonException("Skin package exceeds the 64 KiB limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 4,
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        RequireObject(root, "package");
        int? schemaVersion = null;
        var defaults = SkinPresets.WarmWhite;
        var name = defaults.Name;
        var light = defaults.Light;
        var dark = defaults.Dark;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            var fieldName = ReadFieldName(field);
            RequireUnique(seen, fieldName);
            switch (fieldName)
            {
                case "schemaVersion":
                    if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out var version)
                        || version != CurrentSchemaVersion)
                        throw new JsonException("Unsupported skin schema version. Expected schemaVersion 1.");
                    schemaVersion = version;
                    break;
                case "name":
                    if (field.Value.ValueKind != JsonValueKind.String) throw new JsonException("Skin name must be a string.");
                    name = ReadString(field.Value);
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
                        throw new JsonException("Skin name must contain 1–80 readable characters.");
                    break;
                case "light": light = ReadColors(field.Value, defaults.Light); break;
                case "dark": dark = ReadColors(field.Value, defaults.Dark); break;
                default: throw new JsonException("Unknown skin field.");
            }
        }
        if (schemaVersion is null) throw new JsonException("Skin package requires schemaVersion.");
        return new(schemaVersion.Value, name, light, dark);
    }

    public static bool TryDeserialize(string? json, out SkinPackage? package, out string? error)
    {
        package = null;
        error = null;
        if (json is null)
        {
            error = "Skin package is required.";
            return false;
        }
        try
        {
            package = Deserialize(json);
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static string Serialize(SkinPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        // Validate again at the export/persistence boundary. SkinColors supports safe
        // value-copy editing, but a caller must not be able to export alpha colors.
        package.Light.Validate();
        package.Dark.Validate();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", package.SchemaVersion);
            writer.WriteString("name", package.Name);
            writer.WritePropertyName("light");
            WriteColors(writer, package.Light);
            writer.WritePropertyName("dark");
            WriteColors(writer, package.Dark);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static JsonElement ToJsonElement(SkinPackage package)
    {
        using var document = JsonDocument.Parse(Serialize(package));
        return document.RootElement.Clone();
    }

    private static SkinColors ReadColors(JsonElement element, SkinColors defaults)
    {
        RequireObject(element, "color group");
        var colors = defaults;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            var fieldName = ReadFieldName(field);
            RequireUnique(seen, fieldName);
            if (field.Value.ValueKind != JsonValueKind.String || !SkinColors.TryParseRgb(ReadString(field.Value), out var color))
                throw new JsonException("Skin colors must be opaque #RRGGBB strings.");
            colors = fieldName switch
            {
                "canvas" => colors with { Canvas = color },
                "navigation" => colors with { Navigation = color },
                "chrome" => colors with { Chrome = color },
                "content" => colors with { Content = color },
                "card" => colors with { Card = color },
                "floating" => colors with { Floating = color },
                "input" => colors with { Input = color },
                "text" => colors with { Text = color },
                "muted" => colors with { Muted = color },
                "line" => colors with { Line = color },
                "accent" => colors with { Accent = color },
                "selection" => colors with { Selection = color },
                _ => throw new JsonException("Unknown skin color token."),
            };
        }
        return colors;
    }

    private static void WriteColors(Utf8JsonWriter writer, SkinColors colors)
    {
        writer.WriteStartObject();
        writer.WriteString("canvas", SkinColors.ToHex(colors.Canvas));
        writer.WriteString("navigation", SkinColors.ToHex(colors.Navigation));
        writer.WriteString("chrome", SkinColors.ToHex(colors.Chrome));
        writer.WriteString("content", SkinColors.ToHex(colors.Content));
        writer.WriteString("card", SkinColors.ToHex(colors.Card));
        writer.WriteString("floating", SkinColors.ToHex(colors.Floating));
        writer.WriteString("input", SkinColors.ToHex(colors.Input));
        writer.WriteString("text", SkinColors.ToHex(colors.Text));
        writer.WriteString("muted", SkinColors.ToHex(colors.Muted));
        writer.WriteString("line", SkinColors.ToHex(colors.Line));
        writer.WriteString("accent", SkinColors.ToHex(colors.Accent));
        writer.WriteString("selection", SkinColors.ToHex(colors.Selection));
        writer.WriteEndObject();
    }

    private static string ReadFieldName(JsonProperty property)
    {
        try { return property.Name; }
        catch (InvalidOperationException error)
        {
            throw new JsonException("Skin field names must contain valid Unicode.", error);
        }
    }

    private static string ReadString(JsonElement element)
    {
        try { return element.GetString()!; }
        catch (InvalidOperationException error)
        {
            // JsonDocument can defer validation of escaped surrogate pairs until
            // a string is read. Keep malformed packages inside the JSON failure contract.
            throw new JsonException("Skin strings must contain valid Unicode.", error);
        }
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException($"Skin {name} must be an object.");
    }

    private static void RequireUnique(HashSet<string> seen, string name)
    {
        if (!seen.Add(name)) throw new JsonException("Duplicate skin field.");
    }
}
