using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FilesMate.Platform.Windows.Shell;

[SupportedOSPlatform("windows")]
public static class AppUserModel
{
    public const string Id = "FilesMate.App";

    public static void RegisterCurrentProcess() =>
        _ = SetCurrentProcessExplicitAppUserModelID(Id);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}
