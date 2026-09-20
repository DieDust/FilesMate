namespace FilesMate.App.Navigation;

/// <summary>
/// Path helpers for navigation. Win32 normalization lives in the platform layer.
/// </summary>
public interface IPathService
{
    public string Normalize(string path);

    public string? GetParent(string path);

    public string Combine(string directory, string name);

    public bool IsSamePath(string left, string right);
}
