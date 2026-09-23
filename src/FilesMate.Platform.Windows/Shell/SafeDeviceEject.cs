using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Shell;

public sealed record DeviceEjectTarget(string Location, string Identity, IReadOnlyList<string> AffectedRoots, bool Optical = false);
public sealed record DeviceEjectResult(bool Succeeded, uint ErrorCode = 0, int Veto = 0, string? BlockingName = null)
{
    public string? BlockingDeviceName { get; init; }
    public bool BlockingUsbDebugging { get; init; }
}

/// <summary>Uses PnP's query-remove protocol. Never disables hardware, kills owners, or force-dismounts a volume.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class SafeDeviceEject
{
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
    private static readonly Guid PortableInterface = new(PortableDeviceLocation.InterfaceId);

    public static async Task<DeviceEjectTarget?> QueryAsync(string location, CancellationToken token = default)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        var task = Task.Run(() => { try { return Query(location); } finally { Gate.Release(); } });
        return await task.WaitAsync(token).ConfigureAwait(false);
    }

    public static async Task<DeviceEjectResult> RequestAsync(DeviceEjectTarget expected)
    {
        // Re-resolve identity immediately before removal. A reused drive letter must
        // never eject a different device from the one shown in the menu.
        var current = await QueryAsync(expected.Location).ConfigureAwait(false);
        if (current is null || !string.Equals(current.Identity, expected.Identity, StringComparison.OrdinalIgnoreCase))
            return new(false, 13);
        if (current.Optical)
        {
            await DriveShell.EjectAsync(current.Location).ConfigureAwait(false);
            return new(true);
        }
        return await Task.Run(() =>
        {
            var locate = CM_Locate_DevNodeW(out var node, current.Identity, 0);
            if (locate != 0) return new DeviceEjectResult(false, locate);
            var name = new StringBuilder(260);
            var result = CM_Request_Device_EjectW(node, out var veto, name, name.Capacity, 0);
            var outcome = new DeviceEjectResult(result == 0, result, veto, name.ToString());
            return result != 0 && veto == 5 ? DescribeOpenHandleBlocker(outcome) : outcome;
        }).ConfigureAwait(false);
    }

    internal static DeviceEjectResult DescribeOpenHandleBlocker(DeviceEjectResult result)
    {
        // For OutstandingOpen, Windows returns a device instance ID, not the
        // process name. Resolve that exact interface; never guess from adb.exe
        // merely running or from an unrelated device on the same USB controller.
        if (result.Succeeded || result.Veto != 5 || string.IsNullOrWhiteSpace(result.BlockingName)
            || CM_Locate_DevNodeW(out var node, result.BlockingName, 0) != 0) return result;
        var name = ReadStringProperty(node, 0xD).FirstOrDefault() ?? ReadStringProperty(node, 1).FirstOrDefault();
        return result with
        {
            BlockingDeviceName = name,
            BlockingUsbDebugging = ReadStringProperty(node, 3).Any(IsAdbCompatibleId),
        };
    }

    internal static bool IsAdbCompatibleId(string id) => id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
        && (id[4..].Equals("Class_ff&SubClass_42&Prot_01", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("&Class_ff&SubClass_42&Prot_01", StringComparison.OrdinalIgnoreCase));

    private static string[] ReadStringProperty(uint node, uint property)
    {
        uint size = 0;
        _ = GetBufferProperty(node, property, out _, null, ref size, 0);
        if (size is 0 or > 65536 || (size & 1) != 0) return [];
        var buffer = new byte[size];
        if (GetBufferProperty(node, property, out var type, buffer, ref size, 0) != 0
            || type is not (1 or 7) || size > buffer.Length || (size & 1) != 0) return [];
        return Encoding.Unicode.GetString(buffer, 0, (int)size).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static DeviceEjectTarget? Query(string location)
    {
        if (PortableDeviceLocation.TryParse(location, out var portable))
        {
            var path = portable.Root[PortableDeviceLocation.ComputerPrefix.Length..];
            foreach (var item in Interfaces(PortableInterface))
                if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase) && RemovalIdentity(item.Node) is { } id)
                    return new(location, id, [(portable with { Segments = [] }).Uri]);
            return null;
        }
        if (!IsDriveRoot(location)) return null;
        var drive = new DriveInfo(location);
        if (drive.DriveType == DriveType.CDRom) return new(location, "optical:" + drive.Name, [drive.Name], true);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) return null;
        var number = DeviceNumber(@"\\.\" + drive.Name[..2]);
        if (number is null) return null;
        var disks = Interfaces(DiskInterface).Select(d => (d.Node, Number: DeviceNumber(d.Path))).ToArray();
        var match = disks.FirstOrDefault(d => d.Number == number);
        if (match.Node == 0 || RemovalIdentity(match.Node) is not { } identity) return null;
        var numbers = disks.Where(d => RemovalIdentity(d.Node) == identity).Select(d => d.Number).ToHashSet();
        var roots = DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Where(d => DeviceNumber(@"\\.\" + d.Name[..2]) is { } n && numbers.Contains(n)).Select(d => d.Name).ToArray();
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (roots.Any(r => string.Equals(r, systemRoot, StringComparison.OrdinalIgnoreCase))) return null;
        return new(location, identity, roots);
    }

    public static bool IsDriveRoot(string value) => value.Length is 2 or 3
        && char.IsAsciiLetter(value[0]) && value[1] == ':' && (value.Length == 2 || value[2] == '\\');

    private static string? RemovalIdentity(uint node)
    {
        for (var depth = 0; depth < 8 && node != 0; depth++)
        {
            var id = new StringBuilder(512);
            if (CM_Get_Device_IDW(node, id, id.Capacity, 0) != 0) return null;
            var text = id.ToString();
            // Stop before reaching a hub/controller: removing one must not affect
            // unrelated devices. Only a removable storage/device node is eligible.
            if (text.StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)) return null;
            var service = new StringBuilder(256);
            uint serviceLength = 512;
            if (GetStringProperty(node, 5, out _, service, ref serviceLength, 0) == 0
                && service.ToString().StartsWith("USBHUB", StringComparison.OrdinalIgnoreCase)) return null;
            uint length = 4;
            if (CM_Get_DevNode_Registry_PropertyW(node, 0x10, out _, out var caps, ref length, 0) == 0
                && (caps & 4) != 0) return text;
            if (CM_Get_Parent(out node, node, 0) != 0) break;
        }
        return null;
    }

    private static (uint Type, uint Number)? DeviceNumber(string path)
    {
        using var handle = CreateFileW(path, 0, 7, 0, 3, 0, 0);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x002D1080, 0, 0, out var number, 12, out var bytes, 0) || bytes < 12)
            return null;
        return (number.DeviceType, number.Number);
    }

    private static IReadOnlyList<(string Path, uint Node)> Interfaces(Guid kind)
    {
        var set = SetupDiGetClassDevsW(ref kind, null, 0, 0x12);
        if (set == -1) return [];
        List<(string, uint)> result = [];
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref kind, i, ref data)) break;
                var info = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
                SetupDiGetDeviceInterfaceDetailW(set, ref data, 0, 0, out var size, ref info);
                if (size < 8 || size > 65536) continue;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, size, out _, ref info)
                        && Marshal.PtrToStringUni(buffer + 4) is { } path) result.Add((path, info.Node));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Class; public uint Flags; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public int Size; public Guid Class; public uint Node; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct StorageNumber { public uint DeviceType; public uint Number; public uint Partition; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevsW(ref Guid kind, string? enumerator, nint owner, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid kind, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref InterfaceData data, nint detail, uint size, out uint required, ref DeviceInfo info);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, nint input, uint inSize, out StorageNumber output, uint outSize, out uint returned, nint overlapped);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, int length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type, out uint value, ref uint length, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Registry_PropertyW", CharSet = CharSet.Unicode)] private static extern uint GetStringProperty(uint node, uint property, out uint type, StringBuilder value, ref uint length, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Registry_PropertyW")] private static extern uint GetBufferProperty(uint node, uint property, out uint type, [Out] byte[]? value, ref uint length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Request_Device_EjectW(uint node, out int veto, StringBuilder name, int length, uint flags);
}
