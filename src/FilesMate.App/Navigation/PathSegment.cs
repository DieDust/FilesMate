namespace FilesMate.App.Navigation;

public sealed class PathSegment
{
    public PathSegment(string name, string path, string? glyph = null)
    {
        Name = name;
        Path = path;
        Glyph = glyph ?? string.Empty;
    }

    public string Name { get; }

    public string Path { get; }

    public string Glyph { get; }

    public string Location => Path;

    public bool HasIcon => Glyph.Length > 0;

    public override string ToString() => Name;
}
