using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Interop;

internal static partial class Shell32
{
    internal const uint FoDelete = 3;
    // FOF_ALLOWUNDO on FO_DELETE means Recycle Bin, not Ctrl+Z.
    internal const ushort FofAllowUndo = 0x40;
    internal const ushort FofNoConfirmation = 0x10;
    internal const ushort FofNoErrorUi = 0x0400;
    internal const ushort FofWantNukeWarning = 0x4000;
    internal const int ShopFilePath = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public nint pFrom;
        public nint pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SHObjectProperties(
        nint hwnd,
        int shopObjectType,
        string pszObjectName,
        string? pszPropertyPage);

    internal const uint ShcneAssocChanged = 0x08000000;
    internal const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll")]
    internal static extern void SHChangeNotify(uint wEventId, uint uFlags, nint dwItem1, nint dwItem2);
}
