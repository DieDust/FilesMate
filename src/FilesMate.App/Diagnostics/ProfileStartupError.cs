using System.Runtime.InteropServices;

namespace FilesMate.App.Diagnostics;

internal static class ProfileStartupError
{
    internal static void Show(string details)
    {
        // Profile initialization happens before WinUI and before any log can be opened.
        MessageBoxW(0, "FilesMate could not open its profile.\n\n" + details +
            "\n\nFor the portable ZIP, extract it to a writable folder and keep the data folder beside the application.",
            "FilesMate", 0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
