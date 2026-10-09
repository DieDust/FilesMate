using System.Runtime.InteropServices;
using FilesMate.Platform.Windows.Interop;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Shell;

/// <summary>A filesystem-only picker with an explicit, per-invocation starting folder.</summary>
public static class WindowsFilePicker
{
    public static async Task<string?> PickAsync(nint owner, string startFolder, string title,
        string filterName, string filterPattern, string hint, string unavailableHint)
    {
        if (owner == 0) throw new ArgumentException("A picker owner window is required.", nameof(owner));
        string? selected = null;
        await ShellOperationWorker.RunAsync(() => selected = Show(owner, startFolder, title,
            filterName, filterPattern, hint, unavailableHint)).ConfigureAwait(false);
        return selected;
    }

    private static string? Show(nint owner, string startFolder, string title,
        string filterName, string filterPattern, string hint, string unavailableHint)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var dialog = (IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), throwOnError: true)!)!;
        try
        {
            dialog.GetOptions(out var options);
            // FOS_NOCHANGEDIR | FORCEFILESYSTEM | PATHMUSTEXIST | FILEMUSTEXIST.
            dialog.SetOptions(options | 0x8 | 0x40 | 0x800 | 0x1000);
            dialog.SetTitle(title);
            dialog.SetFileTypes(1, [new() { Name = filterName, Pattern = filterPattern }]);
            dialog.SetFileTypeIndex(1);
            var custom = (IFileDialogCustomize)dialog;
            custom.AddText(1, hint);
            if (!TrySetFolder(dialog, startFolder))
            {
                // The original folder may have disappeared or become inaccessible.
                // Keep browsing possible, but make the fallback visible in the picker.
                TrySetFolder(dialog, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                custom.AddText(2, unavailableHint);
            }
            var result = dialog.Show(owner);
            if (result == unchecked((int)0x800704C7)) return null; // ERROR_CANCELLED
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(0x80058000, out var name); // SIGDN_FILESYSPATH
                try { return Marshal.PtrToStringUni(name); }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    private static bool TrySetFolder(IFileDialog dialog, string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        ShellFileOperation.IShellItem? folder = null;
        try
        {
            ShellFileOperation.SHCreateItemFromParsingName(path, 0, ShellFileOperation.ShellItemId, out folder);
            // SetFolder overrides the dialog's last-used directory. SetDefaultFolder
            // and WinRT SuggestedStartLocation do not satisfy this interaction.
            dialog.SetFolder(folder);
            return true;
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException)
        { return false; }
        finally { if (folder is not null) Marshal.ReleaseComObject(folder); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Pattern;
    }

    // The declarations preserve the native vtable order, including unused slots.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] public int Show(nint owner);
        public void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] filters);
        public void SetFileTypeIndex(uint index);
        public void GetFileTypeIndex(out uint index);
        public void Advise(nint events, out uint cookie);
        public void Unadvise(uint cookie);
        public void SetOptions(uint options);
        public void GetOptions(out uint options);
        public void SetDefaultFolder(ShellFileOperation.IShellItem folder);
        public void SetFolder(ShellFileOperation.IShellItem folder);
        public void GetFolder(out ShellFileOperation.IShellItem folder);
        public void GetCurrentSelection(out ShellFileOperation.IShellItem item);
        public void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        public void GetFileName(out nint name);
        public void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        public void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        public void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        public void GetResult(out ShellFileOperation.IShellItem item);
        public void AddPlace(ShellFileOperation.IShellItem item, uint placement);
        public void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        public void Close(int result);
        public void SetClientGuid(in Guid guid);
        public void ClearClientData();
        public void SetFilter(nint filter);
    }

    [ComImport, Guid("E6FDD21A-163F-4975-9C8C-A69F1BA37034"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialogCustomize
    {
        public void EnableOpenDropDown(uint id);
        public void AddMenu(uint id, [MarshalAs(UnmanagedType.LPWStr)] string text);
        public void AddPushButton(uint id, [MarshalAs(UnmanagedType.LPWStr)] string text);
        public void AddComboBox(uint id);
        public void AddRadioButtonList(uint id);
        public void AddCheckButton(uint id, [MarshalAs(UnmanagedType.LPWStr)] string text, [MarshalAs(UnmanagedType.Bool)] bool value);
        public void AddEditBox(uint id, [MarshalAs(UnmanagedType.LPWStr)] string text);
        public void AddSeparator(uint id);
        public void AddText(uint id, [MarshalAs(UnmanagedType.LPWStr)] string text);
    }
}
