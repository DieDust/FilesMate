using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using FilesMate.Platform.Windows.Interop;

namespace FilesMate.Platform.Windows.Associations;

/// <summary>
/// Opens classic Explorer without going through Folder\open.
/// explorer.exe with a This PC CLSID re-enters that verb, which is FilesMate
/// while we are the default handler.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ClassicExplorer
{
    public const string ExplorerFileName = "explorer.exe";
    public const string ThisPc = @"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    public const string SeparateArguments = "/n,/separate";
    public const string ExecuteFolderClsid = "11dbb47c-a525-400b-9e80-a54615a090c0";

    private static readonly Guid ClsidExecuteFolder = new(ExecuteFolderClsid);
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IidShellItemArray = new("b63ea76d-1f85-456f-a19c-48159efa858b");

    public static string ExecutablePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), ExplorerFileName);

    public static void Launch()
    {
        if (TryLaunchViaExecuteFolder())
        {
            return;
        }

        LaunchProcess();
    }

    public static Task OpenLocationAsync(string parsingName) => Task.Run(() =>
    {
        // A rooted, separate Explorer view avoids re-entering Folder\open when
        // FilesMate is the default handler. Do not change user associations.
        using var started = Process.Start(CreateLocationStartInfo(parsingName));
        if (started is null) throw new IOException("无法打开这个系统位置。");
    });

    public static ProcessStartInfo CreateLocationStartInfo(string parsingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        if ((!parsingName.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                && !parsingName.StartsWith("::{", StringComparison.Ordinal))
            || parsingName.IndexOfAny(['"', '\r', '\n', '\0', ',']) >= 0)
            throw new ArgumentException("Expected a Shell namespace location.", nameof(parsingName));
        var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false };
        start.ArgumentList.Add("/n,/separate,/root," + parsingName);
        return start;
    }

    public static void Launch(DefaultFolderAssociation association, string executable)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        association.RunWhileSuspended(executable, () =>
        {
            // ponytail: SHChangeNotify is async; 400ms is the settle wait before explorer.exe rereads Folder\open.
            Thread.Sleep(400);
            if (TryLaunchViaExecuteFolder())
            {
                return;
            }

            var before = CountCabinetWindows();
            LaunchProcess();
            WaitForCabinetWindow(before, TimeSpan.FromSeconds(8));
        });
    }

    public static ProcessStartInfo CreateStartInfo() =>
        new()
        {
            FileName = ExecutablePath,
            Arguments = SeparateArguments,
            UseShellExecute = false,
        };

    private static void LaunchProcess()
    {
        using var started = Process.Start(CreateStartInfo());
        if (started is null)
        {
            throw new InvalidOperationException("Classic Explorer launch failed.");
        }
    }

    private static bool TryLaunchViaExecuteFolder()
    {
        Exception? error = null;
        var opened = false;
        var thread = new Thread(() =>
        {
            try
            {
                opened = ExecuteFolder(ThisPc);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null && opened;
    }

    private static bool ExecuteFolder(string parsingName)
    {
        var before = CountCabinetWindows();
        object? command = null;
        object? item = null;
        object? items = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidExecuteFolder, throwOnError: true);
            command = Activator.CreateInstance(type!);
            if (command is null)
            {
                return false;
            }

            var iidItem = IidShellItem;
            if (SHCreateItemFromParsingName(parsingName, 0, ref iidItem, out item) < 0 || item is null)
            {
                return false;
            }

            var iidArray = IidShellItemArray;
            if (SHCreateShellItemArrayFromShellItem(item, ref iidArray, out items) < 0 || items is null)
            {
                return false;
            }

            ((IObjectWithSelection)command).SetSelection(items);
            ((IExecuteCommand)command).SetShowWindow(1);
            ((IExecuteCommand)command).Execute();
        }
        finally
        {
            ReleaseCom(items);
            ReleaseCom(item);
            ReleaseCom(command);
        }

        return WaitForCabinetWindow(before, TimeSpan.FromSeconds(5));
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null)
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static bool WaitForCabinetWindow(int previousCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (CountCabinetWindows() > previousCount)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return CountCabinetWindows() > previousCount;
    }

    private static int CountCabinetWindows()
    {
        var count = 0;
        EnumWindowsProc callback = (hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var name = new StringBuilder(256);
            if (GetClassName(hwnd, name, name.Capacity) > 0)
            {
                var className = name.ToString();
                if (className is "CabinetWClass" or "ExploreWClass")
                {
                    count++;
                }
            }

            return true;
        };
        _ = EnumWindows(callback, 0);
        return count;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszName,
        nint pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(
        [MarshalAs(UnmanagedType.IUnknown)] object psi,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("7F918D10-56FB-4B8C-A247-ED1E49AA99CC")]
    private interface IExecuteCommand
    {
        internal void SetKeyState(uint grfKeyState);

        internal void SetParameters([MarshalAs(UnmanagedType.LPWStr)] string pszParameters);

        internal void SetPosition(POINT pt);

        internal void SetShowWindow(int nShow);

        internal void SetNoShowUI([MarshalAs(UnmanagedType.Bool)] bool fNoShowUI);

        internal void SetDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDirectory);

        internal void Execute();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("1C9CD5BB-98E9-4491-A5C3-51B0858E24AB")]
    private interface IObjectWithSelection
    {
        internal void SetSelection([MarshalAs(UnmanagedType.IUnknown)] object psia);

        internal void GetSelection(in Guid riid, out nint ppv);
    }
}
