using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Archives;

namespace FilesMate.App.Services;

internal static class ArchiveProviderNames
{
    internal static string Name(ArchiveProvider provider) => provider switch
    {
        ArchiveProvider.Automatic => StringTable.Get("Archive_Automatic"),
        ArchiveProvider.BuiltIn => StringTable.Get("Archive_BuiltIn"),
        ArchiveProvider.SevenZip => "7-Zip",
        ArchiveProvider.Zip360 => StringTable.Get("Archive_360"),
        ArchiveProvider.HaoZip => StringTable.Get("Archive_HaoZip"),
        ArchiveProvider.SevenZipCompatible => StringTable.Get("Archive_SevenZipCompatible"),
        ArchiveProvider.Other => StringTable.Get("Archive_OtherApp"),
        _ => provider.ToString(),
    };

    internal static string Hint(ArchiveProvider provider) => StringTable.Get(provider switch
    {
        ArchiveProvider.Automatic => "Archive_AutoHint",
        ArchiveProvider.BuiltIn => "Archive_BuiltinHint",
        ArchiveProvider.Zip360 or ArchiveProvider.HaoZip or ArchiveProvider.Other => "Archive_OpenOnlyHint",
        ArchiveProvider.SevenZipCompatible => "Archive_CompatibleHint",
        ArchiveProvider.WinRAR => "Archive_WinRARHint",
        ArchiveProvider.CompactMate => "Archive_CompactMateHint",
        _ => "Archive_ExternalHint",
    });
}
