using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Shell;

public static partial class PortableDeviceService
{
    // Vtable order and signatures are from the Windows SDK's ShlDisp.idl.
    // Typed dual interfaces avoid the dynamic binder's shared ITypeInfo lifetime
    // while independent enumeration, preview and transfer workers are active.
    // Shell automation RCWs can be shared; leave their lifetime to the CLR.
    // ReleaseComObject here could disconnect a different caller's wrapper.
    [ComImport, Guid("D8F015C0-C278-11CE-A49E-444553540000"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellDispatch
    {
        public object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public object Parent { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] public object NameSpace([MarshalAs(UnmanagedType.Struct)] object directory);
    }

    [ComImport, Guid("F0D2D8EF-3890-11D2-BF8B-00C04FB93661"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellFolder2
    {
        public string Title { [return: MarshalAs(UnmanagedType.BStr)] get; }
        public object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public object Parent { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public object ParentFolder { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] public object Items();
        [return: MarshalAs(UnmanagedType.IDispatch)] public object? ParseName([MarshalAs(UnmanagedType.BStr)] string name);
        public void NewFolder([MarshalAs(UnmanagedType.BStr)] string name, [MarshalAs(UnmanagedType.Struct)] object options);
        public void MoveHere([MarshalAs(UnmanagedType.Struct)] object item, [MarshalAs(UnmanagedType.Struct)] object options);
        public void CopyHere([MarshalAs(UnmanagedType.Struct)] object item, [MarshalAs(UnmanagedType.Struct)] object options);
        [return: MarshalAs(UnmanagedType.BStr)] public string GetDetailsOf([MarshalAs(UnmanagedType.Struct)] object item, int column);
        public object Self { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
    }

    [ComImport, Guid("744129E0-CBE5-11CE-8350-444553540000"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellFolderItems
    {
        public int Count { get; }
        public object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public object Parent { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] public object Item([MarshalAs(UnmanagedType.Struct)] object index);
    }

    [ComImport, Guid("EDC817AA-92B8-11D1-B075-00C04FC33AA5"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellFolderItem2
    {
        public object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public object Parent { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public string Name { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
        public string Path { [return: MarshalAs(UnmanagedType.BStr)] get; }
        public object GetLink { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public object GetFolder { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        public bool IsLink { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        public bool IsFolder { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        public bool IsFileSystem { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        public bool IsBrowsable { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        public DateTime ModifyDate { get; set; }
        public int Size { get; }
        public string Type { [return: MarshalAs(UnmanagedType.BStr)] get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] public object Verbs();
        public void InvokeVerb([MarshalAs(UnmanagedType.Struct)] object verb);
        public void InvokeVerbEx([MarshalAs(UnmanagedType.Struct)] object verb, [MarshalAs(UnmanagedType.Struct)] object args);
        [return: MarshalAs(UnmanagedType.Struct)] public object? ExtendedProperty([MarshalAs(UnmanagedType.BStr)] string name);
    }
}
