using System.Text.Json.Serialization;

namespace FilesMate.TestDataGenerator;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DatasetMarker))]
internal partial class MarkerJsonContext : JsonSerializerContext;
