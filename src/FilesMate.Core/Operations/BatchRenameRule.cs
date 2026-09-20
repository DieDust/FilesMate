namespace FilesMate.Core.Operations;

public sealed record BatchRenameRule
{
    public string? BaseName { get; init; }

    public bool CaseSensitive { get; init; }

    public string Find { get; init; } = string.Empty;

    public string Replace { get; init; } = string.Empty;

    public string Prefix { get; init; } = string.Empty;

    public string Suffix { get; init; } = string.Empty;

    public string? RegexPattern { get; init; }

    public string RegexReplacement { get; init; } = string.Empty;

    public int? NumberStart { get; init; }

    public int NumberWidth { get; init; }

    public bool IncludeExtension { get; init; }

    public bool Uppercase { get; init; }

    public bool Lowercase { get; init; }
}
