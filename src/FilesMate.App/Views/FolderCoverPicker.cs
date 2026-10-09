using FilesMate.App.Icons;
using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Views;

internal static class FolderCoverPicker
{
    private static readonly HashSet<nint> ActiveOwners = [];

    internal static async Task<string?> PickAsync(FrameworkElement host, string folder)
    {
        var owner = App.WindowForElement(host)?.NativeHandle ?? 0;
        if (owner == 0 || !ActiveOwners.Add(owner)) return null;
        try
        {
            var cover = await WindowsFilePicker.PickAsync(owner, folder,
                StringTable.Get("Cover_PickerTitle"), StringTable.Get("Cover_MediaFilter"),
                string.Join(';', FolderCoverSelection.Extensions.Select(extension => "*" + extension)),
                StringTable.Get("Cover_ReferenceHint"), StringTable.Get("Cover_StartUnavailable"));
            if (cover is null || !host.IsLoaded) return null;
            if (!await Task.Run(() => Directory.Exists(folder)))
                throw new IOException(StringTable.Get("Cover_FolderUnavailable"));
            if (!FolderCoverSelection.IsSupported(cover)
                || await ShellIconBinder.GetThumbnailAsync(cover, 96, CancellationToken.None) is null)
                throw new IOException(StringTable.Get("Cover_Unavailable"));
            return cover;
        }
        finally { ActiveOwners.Remove(owner); }
    }
}
