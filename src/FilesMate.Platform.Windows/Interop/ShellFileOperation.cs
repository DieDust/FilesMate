using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Interop;

internal static class ShellFileOperation
{
    internal static readonly Guid ClassId = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    internal static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        public void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
        public void GetParent(out IShellItem parent);
        public void GetDisplayName(uint kind, out nint name);
        public void GetAttributes(uint mask, out uint attributes);
        public void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        public void Advise(IProgressSink sink, out uint cookie);
        public void Unadvise(uint cookie);
        public void SetOperationFlags(uint flags);
        public void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        public void SetProgressDialog(nint dialog);
        public void SetProperties(nint properties);
        public void SetOwnerWindow(uint owner);
        public void ApplyPropertiesToItem(IShellItem item);
        public void ApplyPropertiesToItems(nint items);
        public void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IProgressSink sink);
        public void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        public void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IProgressSink sink);
        public void MoveItems(nint items, IShellItem destination);
        public void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IProgressSink sink);
        public void CopyItems(nint items, IShellItem destination);
        public void DeleteItem(IShellItem item, IProgressSink sink);
        public void DeleteItems(nint items);
        public void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IProgressSink sink);
        [PreserveSig] public int PerformOperations();
        public void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IProgressSink
    {
        [PreserveSig] public int StartOperations();
        [PreserveSig] public int FinishOperations(int result);
        [PreserveSig] public int PreRenameItem(uint flags, nint item, nint newName);
        [PreserveSig] public int PostRenameItem(uint flags, nint item, nint newName, int result, nint created);
        [PreserveSig] public int PreMoveItem(uint flags, nint item, nint destination, nint newName);
        [PreserveSig] public int PostMoveItem(uint flags, nint item, nint destination, nint newName, int result, nint created);
        [PreserveSig] public int PreCopyItem(uint flags, nint item, nint destination, nint newName);
        [PreserveSig] public int PostCopyItem(uint flags, nint item, nint destination, nint newName, int result, nint created);
        [PreserveSig] public int PreDeleteItem(uint flags, nint item);
        [PreserveSig] public int PostDeleteItem(uint flags, nint item, int result, nint recycled);
        [PreserveSig] public int PreNewItem(uint flags, nint destination, nint name);
        [PreserveSig] public int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created);
        [PreserveSig] public int UpdateProgress(uint total, uint completed);
        [PreserveSig] public int ResetTimer();
        [PreserveSig] public int PauseTimer();
        [PreserveSig] public int ResumeTimer();
    }
}
