using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FilesMate.Platform.Windows.Shell;

[SupportedOSPlatform("windows")]
public static class ShellShortcut
{
    public static string UniqueLinkPath(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var folder = Path.GetDirectoryName(targetPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(folder))
        {
            throw new IOException("The shortcut has no folder.");
        }

        var stem = Path.GetFileName(targetPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(stem))
        {
            stem = "Shortcut";
        }

        var name = stem + " - Shortcut.lnk";
        var candidate = Path.Combine(folder, name);
        var n = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(folder, stem + " - Shortcut (" + n + ").lnk");
            n++;
        }

        return candidate;
    }

    public static void Create(string targetPath, string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);

        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(targetPath);
            var working = Path.GetDirectoryName(targetPath.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(working))
            {
                link.SetWorkingDirectory(working);
            }

            ((IPersistFile)link).Save(shortcutPath, fRemember: true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        internal void GetPath(nint pszFile, int cch, nint pfd, uint fFlags);
        internal void GetIDList(out nint ppidl);
        internal void SetIDList(nint pidl);
        internal void GetDescription(nint pszName, int cch);
        internal void SetDescription(string pszName);
        internal void GetWorkingDirectory(nint pszDir, int cch);
        internal void SetWorkingDirectory(string pszDir);
        internal void GetArguments(nint pszArgs, int cch);
        internal void SetArguments(string pszArgs);
        internal void GetHotkey(out short pwHotkey);
        internal void SetHotkey(short wHotkey);
        internal void GetShowCmd(out int piShowCmd);
        internal void SetShowCmd(int iShowCmd);
        internal void GetIconLocation(nint pszIconPath, int cch, out int piIcon);
        internal void SetIconLocation(string pszIconPath, int iIcon);
        internal void SetRelativePath(string pszPathRel, uint dwReserved);
        internal void Resolve(nint hwnd, uint fFlags);
        internal void SetPath(string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        internal void GetClassID(out Guid pClassID);
        [PreserveSig]
        internal int IsDirty();
        internal void Load(string pszFileName, uint dwMode);
        internal void Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        internal void SaveCompleted(string pszFileName);
        internal void GetCurFile(out nint ppszFileName);
    }
}
