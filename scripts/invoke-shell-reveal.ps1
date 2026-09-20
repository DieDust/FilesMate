#Requires -Version 7
param([Parameter(Mandatory)][string]$Path, [switch]$SingleItemPidl)
$ErrorActionPreference = 'Stop'
# Exercise the same Windows API used by Chromium's ShowItemInFolder.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeShellReveal {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern IntPtr ILCreateFromPath(string path);
    [DllImport("shell32.dll")] static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] children, uint flags);
    [DllImport("shell32.dll")] static extern void ILFree(IntPtr value);
    public static int Reveal(string path, bool singleItem) {
        var item = ILCreateFromPath(path);
        if (item == IntPtr.Zero) throw new ArgumentException("The target has no shell ID.");
        IntPtr parent = IntPtr.Zero;
        try {
            if (singleItem) return SHOpenFolderAndSelectItems(item, 0, null, 0);
            parent = ILCreateFromPath(System.IO.Path.GetDirectoryName(path));
            if (parent == IntPtr.Zero) throw new ArgumentException("The parent has no shell ID.");
            return SHOpenFolderAndSelectItems(parent, 1, new[] { item }, 0);
        }
        finally { ILFree(item); if (parent != IntPtr.Zero) ILFree(parent); }
    }
}
'@
$result = [NativeShellReveal]::Reveal([IO.Path]::GetFullPath($Path), $SingleItemPidl.IsPresent)
if ($result -lt 0) { throw ('SHOpenFolderAndSelectItems failed: 0x{0:X8}' -f $result) }
Write-Output 'SHOpenFolderAndSelectItems succeeded.'
