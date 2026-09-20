using System.Runtime.Versioning;

using Windows.UI.StartScreen;

namespace FilesMate.App.Services;

/// <summary>
/// Updates the taskbar jump list with recent folders, matching Explorer's Frequent section.
/// </summary>
[SupportedOSPlatform("windows10.0.10586.0")]
public static class JumpListService
{
    public const string RecentGroup = "RecentFolders";

    [SupportedOSPlatform("windows10.0.10586.0")]
    public static async Task ApplyRecentFoldersAsync(IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10586))
        {
            return;
        }

        try
        {
            var jumpList = await JumpList.LoadCurrentAsync().AsTask().ConfigureAwait(false);
            jumpList.Items.Clear();
            foreach (var path in folders)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var name = Path.GetFileName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name))
                {
                    name = path;
                }

                var item = JumpListItem.CreateWithArguments("/open \"" + path + "\"", name);
                item.Description = path;
                item.GroupName = RecentGroup;
                // Leaving Logo unset uses the application icon. Absolute file
                // URIs (and ICO files) are not accepted by JumpListItem.Logo.
                jumpList.Items.Add(item);
            }

            jumpList.SystemGroupKind = JumpListSystemGroupKind.Recent;
            await jumpList.SaveAsync().AsTask().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceError("Jump list update failed: {0}", ex);
        }
    }
}
