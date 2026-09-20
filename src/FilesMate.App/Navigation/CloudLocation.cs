using System.IO;

namespace FilesMate.App.Navigation;

public static class CloudLocation
{
    public const string Glyph = "\uE753";

    public static IEnumerable<string> CandidatePaths()
    {
        foreach (var key in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" })
        {
            var path = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            yield return Path.Combine(profile, "Google Drive");
            yield return Path.Combine(profile, "Google Drive for desktop");
            yield return Path.Combine(profile, "My Drive");
            yield return Path.Combine(profile, "Dropbox");
            yield return Path.Combine(profile, "iCloudDrive");
            yield return Path.Combine(profile, "iCloud Drive");
            yield return Path.Combine(profile, "WPS Cloud");
            yield return Path.Combine(profile, "WPS云盘");
            yield return Path.Combine(profile, "WPS云文档");
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
        {
            yield return Path.Combine(documents, "WPS Cloud");
            yield return Path.Combine(documents, "WPS云盘");
        }
    }

    public static bool IsRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || HomeLocation.IsHome(path) || TagLocation.IsTag(path))
        {
            return false;
        }

        foreach (var candidate in CandidatePaths())
        {
            if (PinnedLocationStore.PathsEqual(path, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
