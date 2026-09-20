using Loc = FilesMate.App.Localization.StringTable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FilesMate.Platform.Windows.Processes;
using FilesMate.Search;

namespace FilesMate.SearchHost;

internal static class ApplicationShell
{
    internal static Task RunAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new System.Threading.Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "FilesMate application launch" };
        worker.SetApartmentState(System.Threading.ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    internal static void Launch(ApplicationEntry entry)
    {
        if (!entry.LaunchPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            DetachedProcess.Open(entry.LaunchPath);
            return;
        }
        // Launching in-process makes the application our child, so it would inherit any kill-on-close job we
        // could not leave. Explorer sits outside such jobs; let it invoke the AppsFolder item for us.
        if (DetachedProcess.IsInKillOnCloseJob() && DetachedProcess.TryOpenViaExplorer(entry.LaunchPath)) return;
        // Shell.Application can return the same RCW to the catalog and launcher
        // threads. FinalReleaseComObject on either thread then disconnects the
        // other caller while it is still invoking the item. Let the runtime own
        // these short-lived RCWs instead of force-releasing a shared wrapper.
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
        var folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
        var item = ((dynamic)folder!).ParseName(entry.Id);
        if (item is null) throw new IOException(Loc.Get("App_EntryUnavailable"));
        ((dynamic)item).InvokeVerb("open");
    }

    internal static BitmapSource? Icon(string parsingName)
    {
        var initialized = CoInitializeEx(0, 0);
        nint pidl = 0;
        var info = new ShellFileInfo();
        try
        {
            if (SHParseDisplayName(parsingName, 0, out pidl, 0, out _) < 0 || pidl == 0) return null;
            if (SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100 | 0x8) == 0 || info.Icon == 0) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            if (info.Icon != 0) DestroyIcon(info.Icon);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (initialized >= 0) CoUninitialize();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint flags, out uint attributes);
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(nint pidl, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
