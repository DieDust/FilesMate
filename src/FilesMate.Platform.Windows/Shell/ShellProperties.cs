using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Shell;

public sealed record ShellPropertyColumn(string Name, string Title, double Width);

/// <summary>Windows' registered column catalog and read-only, on-demand property handlers.</summary>
[SupportedOSPlatform("windows")]
public static class ShellProperties
{
    private static readonly Lazy<Task<IReadOnlyList<ShellPropertyColumn>>> Catalog = new(LoadCatalogAsync);
    private static readonly SemaphoreSlim Readers = new(3);
    public static Task<IReadOnlyList<ShellPropertyColumn>> GetColumnsAsync() => Catalog.Value;

    private static async Task<IReadOnlyList<ShellPropertyColumn>> LoadCatalogAsync()
    {
        var result = new List<ShellPropertyColumn>();
        await ShellOperationWorker.RunAsync(() =>
        {
            using var apartment = new ComApartment();
            var iid = typeof(IPropertyDescriptionList).GUID;
            Marshal.ThrowExceptionForHR(PSEnumeratePropertyDescriptions(6, ref iid, out var list));
            try
            {
                list.GetCount(out var count);
                var descriptionId = typeof(IPropertyDescription).GUID;
                for (uint i = 0; i < count; i++)
                {
                    if (list.GetAt(i, ref descriptionId, out var description) < 0) continue;
                    try
                    {
                        if (description.GetCanonicalName(out var namePointer) < 0) continue;
                        var name = TakeString(namePointer);
                        if (description.GetDisplayName(out var titlePointer) < 0) continue;
                        var title = TakeString(titlePointer);
                        description.GetDefaultColumnWidth(out var characters);
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(title))
                            result.Add(new(name, title, Math.Clamp(characters * 8d, 96, 360)));
                    }
                    finally { Marshal.ReleaseComObject(description); }
                }
            }
            finally { Marshal.ReleaseComObject(list); }
        }).ConfigureAwait(false);
        return result.DistinctBy(p => p.Name).OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static async Task<IReadOnlyDictionary<string, EntryPropertyValue>> ReadAsync(string path,
        IReadOnlyList<string> properties, CancellationToken cancellationToken = default)
    {
        if (properties.Count == 0 || !Path.IsPathFullyQualified(path) || PortableDeviceLocation.TryParse(path, out _))
            return new Dictionary<string, EntryPropertyValue>();
        await Readers.WaitAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, EntryPropertyValue>(StringComparer.Ordinal);
        try
        {
            await ShellOperationWorker.RunAsync(() =>
            {
                using var apartment = new ComApartment();
                cancellationToken.ThrowIfCancellationRequested();
                // Do not hydrate cloud placeholders just to paint a metadata column.
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.Offline | (FileAttributes)0x440000)) != 0) return;
                var iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreFromParsingName(path, 0, 0x60, ref iid, out var store) < 0) return;
                try
                {
                    foreach (var name in properties.Distinct())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (PSGetPropertyKeyFromName(name, out var key) < 0) continue;
                        var value = default(PropVariant);
                        try
                        {
                            if (store.GetValue(ref key, out value) < 0 || value.Type is 0 or 1) continue;
                            var text = PSFormatForDisplayAlloc(ref key, ref value, 0, out var formatted) >= 0
                                ? TakeString(formatted) : "";
                            decimal? number = value.Type switch
                            {
                                2 => value.Int16, 3 => value.Int32, 16 => value.Int8, 17 => value.UInt8,
                                18 => value.UInt16, 19 => value.UInt32, 20 => value.Int64, 21 => value.UInt64,
                                4 when float.IsFinite(value.Float) && Math.Abs(value.Float) < (float)decimal.MaxValue => (decimal)value.Float,
                                5 when double.IsFinite(value.Double) && Math.Abs(value.Double) < (double)decimal.MaxValue => (decimal)value.Double,
                                11 => value.Int16 != 0 ? 1 : 0, _ => null
                            };
                            result[name] = new(text, number, value.Type == 64 ? value.Int64 : null);
                        }
                        finally { PropVariantClear(ref value); }
                    }
                }
                finally { Marshal.ReleaseComObject(store); }
            }).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or ArgumentException or OverflowException)
        {
            // Unavailable properties stay empty, like Explorer. A failed handler must not block navigation.
        }
        finally { Readers.Release(); }
        return result;
    }

    private static string TakeString(nint value)
    {
        try { return Marshal.PtrToStringUni(value) ?? ""; }
        finally { Marshal.FreeCoTaskMem(value); }
    }
    private sealed class ComApartment : IDisposable
    {
        private readonly bool _initialized = CoInitializeEx(0, 2) >= 0;
        public void Dispose() { if (_initialized) CoUninitialize(); }
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public sbyte Int8;
        [FieldOffset(8)] public byte UInt8;
        [FieldOffset(8)] public short Int16;
        [FieldOffset(8)] public ushort UInt16;
        [FieldOffset(8)] public int Int32;
        [FieldOffset(8)] public uint UInt32;
        [FieldOffset(8)] public long Int64;
        [FieldOffset(8)] public ulong UInt64;
        [FieldOffset(8)] public float Float;
        [FieldOffset(8)] public double Double;
    }
    [ComImport, Guid("1F9FC1D0-C39B-4B26-817F-011967D3440E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyDescriptionList
    {
        [PreserveSig] public int GetCount(out uint count);
        [PreserveSig] public int GetAt(uint index, ref Guid iid, out IPropertyDescription description);
    }
    [ComImport, Guid("6F79D558-3E96-4549-A1D1-7D75D2288814"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyDescription
    {
        [PreserveSig] public int GetPropertyKey(out PropertyKey key);
        [PreserveSig] public int GetCanonicalName(out nint name);
        [PreserveSig] public int GetPropertyType(out ushort type);
        [PreserveSig] public int GetDisplayName(out nint title);
        [PreserveSig] public int GetEditInvitation(out nint invitation);
        [PreserveSig] public int GetTypeFlags(uint mask, out uint flags);
        [PreserveSig] public int GetViewFlags(out uint flags);
        [PreserveSig] public int GetDefaultColumnWidth(out uint width);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] public int GetCount(out uint count);
        [PreserveSig] public int GetAt(uint index, out PropertyKey key);
        [PreserveSig] public int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [DllImport("propsys.dll")] private static extern int PSEnumeratePropertyDescriptions(int filter, ref Guid iid, out IPropertyDescriptionList list);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] private static extern int PSGetPropertyKeyFromName(string name, out PropertyKey key);
    [DllImport("propsys.dll")] private static extern int PSFormatForDisplayAlloc(ref PropertyKey key, ref PropVariant value, uint flags, out nint text);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetPropertyStoreFromParsingName(string path, nint bindContext, uint flags, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}
