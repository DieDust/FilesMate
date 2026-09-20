using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FilesMate.Search;

/// <summary>Read the stored target only: never resolve or launch a link during search.</summary>
internal static class ApplicationShortcut
{
    private static readonly ConcurrentDictionary<string, (long Checked, bool Application)> Cache = new(StringComparer.OrdinalIgnoreCase);
    public static bool IsApplication(string? path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)
            || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
        var now = Environment.TickCount64;
        if (Cache.TryGetValue(path, out var cached) && now - cached.Checked < 30000) return cached.Application;
        var application = ReadStoredTarget(path) is not null;
        if (Cache.Count >= 2048) Cache.Clear();
        Cache[path] = (now, application);
        return application;
    }

    [SupportedOSPlatform("windows")]
    internal static (string Target, string Arguments)? ReadStoredTarget(string path)
    {
        object? instance = null;
        nint buffer = 0;
        try
        {
            instance = new ShellLinkCoClass();
            ((IPersistFile)instance).Load(path, 0);
            buffer = Marshal.AllocCoTaskMem(32768 * 2);
            Marshal.WriteInt16(buffer, 0);
            ((IShellLinkW)instance).GetPath(buffer, 32768, 0, 4); // SLGP_RAWPATH; no target resolution/network access.
            var target = Environment.ExpandEnvironmentVariables(Marshal.PtrToStringUni(buffer) ?? "");
            if (Path.GetExtension(target).ToLowerInvariant() is not (".exe" or ".com" or ".bat" or ".cmd" or ".appref-ms")) return null;
            Marshal.WriteInt16(buffer, 0);
            ((IShellLinkW)instance).GetArguments(buffer, 32768);
            return (target, Marshal.PtrToStringUni(buffer) ?? "");
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        finally
        {
            if (buffer != 0) Marshal.FreeCoTaskMem(buffer);
            if (instance is not null) Marshal.FinalReleaseComObject(instance);
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
