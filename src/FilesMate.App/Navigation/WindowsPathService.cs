using System.IO;

using FilesMate.Platform.Windows.Paths;

namespace FilesMate.App.Navigation;

public sealed class WindowsPathService : IPathService
{
    private readonly WindowsPathNormalizer _normalizer = new();

    public string Normalize(string path)
    {
        if (HomeLocation.IsHome(path))
        {
            return HomeLocation.Uri;
        }

        if (TagLocation.IsTag(path))
        {
            return TagLocation.TryParse(path, out var id) ? TagLocation.Uri(id) : path.Trim();
        }

        return _normalizer.Normalize(path);
    }

    public string? GetParent(string path)
    {
        if (HomeLocation.IsHome(path) || TagLocation.IsTag(path))
        {
            return HomeLocation.IsHome(path) ? null : HomeLocation.Uri;
        }

        return _normalizer.GetParent(path);
    }

    public string Combine(string directory, string name)
    {
        if (HomeLocation.IsHome(directory))
        {
            throw new ArgumentException("Home has no child paths.", nameof(directory));
        }

        if (TagLocation.IsTag(directory))
        {
            return Path.IsPathRooted(name) ? name : Path.Combine(directory, name);
        }

        return _normalizer.Combine(directory, name);
    }

    public bool IsSamePath(string left, string right)
    {
        if (HomeLocation.IsHome(left) || HomeLocation.IsHome(right))
        {
            return HomeLocation.IsHome(left) && HomeLocation.IsHome(right);
        }

        if (TagLocation.IsTag(left) || TagLocation.IsTag(right))
        {
            return TagLocation.TryParse(left, out var leftId)
                && TagLocation.TryParse(right, out var rightId)
                && leftId == rightId;
        }

        return _normalizer.Equals(left, right);
    }
}
