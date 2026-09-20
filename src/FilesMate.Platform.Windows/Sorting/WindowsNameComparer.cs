using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Sorting;

/// <summary>
/// Windows Explorer-style name comparison via <c>StrCmpLogicalW</c>.
/// </summary>
public sealed partial class WindowsNameComparer : IComparer<string>
{
    public static WindowsNameComparer Instance { get; } = new();

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string psz1, string psz2);
}
