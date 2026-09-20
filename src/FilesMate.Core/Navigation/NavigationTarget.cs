namespace FilesMate.Core.Navigation;

/// <summary>
/// User-facing navigation destination. Paths are stored as provided after rejecting empty values;
/// Win32 normalization happens in the platform layer.
/// Thread-safety: immutable. Staleness: the path may not exist by the time enumeration starts.
/// </summary>
public sealed record NavigationTarget
{
    private NavigationTarget(string path, NavigationKind kind)
    {
        Path = path;
        Kind = kind;
    }

    public string Path { get; }

    public NavigationKind Kind { get; }

    public static NavigationTarget FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Navigation path is empty.", nameof(path));
        }

        return new NavigationTarget(path, NavigationKind.ExplicitPath);
    }

    public static NavigationTarget Up(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            throw new ArgumentException("Current path is empty.", nameof(currentPath));
        }

        return new NavigationTarget(currentPath, NavigationKind.Up);
    }
}

public enum NavigationKind
{
    ExplicitPath = 0,
    Up = 1,
    Back = 2,
    Forward = 3,
    Refresh = 4,
}
