namespace FilesMate.TestDataGenerator;

public enum DatasetProfile
{
    Empty,
    Mixed,
    Images,
    Unicode,
    LongPaths,
}

public sealed record GenerationOptions
{
    public required string Root { get; init; }

    public int FileCount { get; init; }

    public int DirectoryCount { get; init; }

    public int Depth { get; init; } = 1;

    public int Seed { get; init; }

    public DatasetProfile Profile { get; init; } = DatasetProfile.Mixed;

    public IReadOnlyDictionary<string, int>? ExtensionWeights { get; init; }
}

public sealed class DatasetGenerationResult
{
    public required string Root { get; init; }

    public required string MarkerPath { get; init; }

    public int FileCount { get; init; }

    public int DirectoryCount { get; init; }

    public required IReadOnlyList<string> RelativeFileNames { get; init; }
}

public sealed class DatasetMarker
{
    public string Generator { get; set; } = DatasetGenerator.GeneratorId;

    public int Version { get; set; } = DatasetGenerator.MarkerVersion;

    public int Seed { get; set; }

    public string Profile { get; set; } = string.Empty;

    public int FileCount { get; set; }

    public int DirectoryCount { get; set; }

    public int Depth { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
