namespace FilesMate.App.Preview;

public readonly record struct PreviewRequest(
    string Path,
    long Generation,
    int MaxTextBytes = 1_048_576)
{
    public PreviewRequest Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        return this with { MaxTextBytes = Math.Clamp(MaxTextBytes, 1, 16 * 1024 * 1024) };
    }
}
