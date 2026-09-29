using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FilesMate.Platform.Windows.Shell;

[SupportedOSPlatform("windows")]
public static class InstalledFonts
{
    private static readonly Lazy<Task<string[]>> Fonts = new(() => Task.Run(Read));
    public static Task<string[]> GetAsync() => Fonts.Value;

    private static string[] Read()
    {
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var dc = CreateCompatibleDC(0);
        if (dc == 0) return [];
        try
        {
            var filter = new LogFont { CharSet = 1, FaceName = "" };
            FontCallback callback = (font, _, _, _) =>
            {
                var name = Marshal.PtrToStructure<LogFont>(font).FaceName;
                if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) names.Add(name);
                return 1;
            };
            _ = EnumFontFamiliesExW(dc, ref filter, callback, 0, 0);
            GC.KeepAlive(callback);
        }
        finally { _ = DeleteDC(dc); }
        return names.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }
    private delegate int FontCallback(nint font, nint metrics, uint type, nint parameter);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int EnumFontFamiliesExW(nint dc, ref LogFont font, FontCallback callback, nint parameter, uint flags);
}
