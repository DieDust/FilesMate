using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FilesMate.Platform.Windows.Shell;

[SupportedOSPlatform("windows")]
public static class ShellLocation
{
    /// <summary>Resolve redirected known folders; null means a virtual Shell folder.</summary>
    public static Task<string?> ResolveFileSystemPathAsync(string parsingName) => OnSta(() =>
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(parsingName, 0, out var pidl, 0, out _));
        try
        {
            var buffer = new StringBuilder(32768);
            return SHGetPathFromIDListEx(pidl, buffer, (uint)buffer.Capacity, 0) ? buffer.ToString() : null;
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    });

    internal static Task<T> OnSta<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(operation()); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "FilesMate Shell location" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindingContext, out nint pidl, uint attributes, out uint actualAttributes);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListEx(nint pidl, StringBuilder path, uint length, uint flags);
}
