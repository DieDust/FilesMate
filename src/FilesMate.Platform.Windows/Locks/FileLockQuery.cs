using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace FilesMate.Platform.Windows.Locks;

public readonly record struct FileLockHandle(int ProcessId, nint Value, string Path);

public sealed record FileLockProcess(
    int ProcessId,
    string Name,
    string? ImagePath,
    string? ServiceName,
    bool CanTerminate,
    IReadOnlyList<string> Paths,
    IReadOnlyList<FileLockHandle> Handles)
{
    public DateTime? StartedAtUtc { get; init; }
}

public static class FileLockQuery
{
    // Inspection uses the caller's existing rights. Only duplicates owned by
    // this query are closed; application handles must be released by their owners.
    public static IReadOnlyList<FileLockProcess> Find(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var targets = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => (Path: FileLockPath.Normalize(path), Directory: Directory.Exists(path)))
            .Distinct()
            .ToArray();
        if (targets.Length == 0)
        {
            return [];
        }

        var rows = new Dictionary<int, Builder>();
        AddRestartManager(targets, rows);
        AddHandleLocks(targets, rows);
        return rows.Values
            .Select(row => row.Build())
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProcessId)
            .ToArray();
    }

    public static IReadOnlyList<FileLockHandle> HandlesFor(FileLockProcess process, string? lockedPath)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (string.IsNullOrWhiteSpace(lockedPath))
        {
            return process.Handles;
        }

        var directory = Directory.Exists(lockedPath);
        return [.. process.Handles.Where(handle =>
            !string.IsNullOrWhiteSpace(handle.Path)
            && FileLockPath.Matches(handle.Path, lockedPath, directory))];
    }

    public static bool Terminate(FileLockProcess expected)
    {
        var processId = expected.ProcessId;
        if (!expected.CanTerminate || expected.StartedAtUtc is null) return false;
        if (processId is 0 or 4 || processId == Environment.ProcessId)
        {
            return false;
        }

        using var process = Process.GetProcessById(processId);
        _ = process.Handle; // Hold this process instance across validation and termination.
        if (process.StartTime.ToUniversalTime() != expected.StartedAtUtc) return false;
        process.Kill(entireProcessTree: false);
        return true;
    }

    private static void AddRestartManager(
        (string Path, bool Directory)[] targets,
        Dictionary<int, Builder> rows)
    {
        var files = RestartManagerFiles(targets);
        var key = new StringBuilder(33);
        if (files.Length == 0 || RmStartSession(out var session, 0, key) != 0)
        {
            return;
        }

        try
        {
            if (RmRegisterResources(session, (uint)files.Length, files, 0, 0, 0, 0) != 0)
            {
                return;
            }

            uint needed = 0;
            uint reboot = 0;
            var count = 0u;
            var status = RmGetList(session, out needed, ref count, null, ref reboot);
            if (status == ErrorMoreData)
            {
                var info = new RM_PROCESS_INFO[Math.Max(needed, 1)];
                count = (uint)info.Length;
                status = RmGetList(session, out needed, ref count, info, ref reboot);
                if (status != 0)
                {
                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    var item = info[i];
                    var pid = (int)item.Process.ProcessId;
                    var row = GetOrAdd(rows, pid, item.AppName, item.ServiceShortName);
                    if (!string.IsNullOrWhiteSpace(item.AppName))
                    {
                        row.Name = item.AppName;
                    }

                    if (!string.IsNullOrWhiteSpace(item.ServiceShortName))
                    {
                        row.ServiceName = item.ServiceShortName;
                    }
                }
            }
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    private static string[] RestartManagerFiles((string Path, bool Directory)[] targets)
    {
        var files = new List<string>();
        foreach (var target in targets)
        {
            files.Add(target.Directory ? target.Path + "\\" : target.Path);
            if (!target.Directory)
            {
                continue;
            }

            // ponytail: Restart Manager does not recurse. Sample the first level
            // so indexers holding children still show up without walking Unreal-sized trees.
            try
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(target.Path).Take(64))
                {
                    files.Add(Directory.Exists(child) ? child + "\\" : child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        return [.. files.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static unsafe void AddHandleLocks(
        (string Path, bool Directory)[] targets,
        Dictionary<int, Builder> rows)
    {
        var snapshot = SnapshotHandles();
        if (snapshot.Length == 0)
        {
            return;
        }

        var fileType = FileTypeIndex(snapshot);
        var grouped = new Dictionary<int, List<nint>>();
        foreach (var entry in snapshot)
        {
            if (fileType is { } type && entry.ObjectTypeIndex != type)
            {
                continue;
            }

            var pid = (int)entry.UniqueProcessId;
            if (pid <= 0)
            {
                continue;
            }

            if (!grouped.TryGetValue(pid, out var handles))
            {
                handles = [];
                grouped[pid] = handles;
            }

            handles.Add(entry.HandleValue);
        }

        foreach (var (pid, handles) in grouped)
        {
            using var process = OpenProcess(DupAccess, false, pid);
            if (process.IsInvalid)
            {
                continue;
            }

            foreach (var value in handles)
            {
                if (NtDuplicateObject(
                        process.DangerousGetHandle(),
                        value,
                        GetCurrentProcess(),
                        out var dup,
                        0,
                        0,
                        DuplicateSameAccess) != 0)
                {
                    continue;
                }

                try
                {
                    if (GetFileType(dup) != FileTypeDisk)
                    {
                        continue;
                    }

                    var path = PathFromHandle(dup);
                    if (path is null || !targets.Any(target => FileLockPath.Matches(path, target.Path, target.Directory)))
                    {
                        continue;
                    }

                    var row = GetOrAdd(rows, pid, name: null, service: null);
                    row.Add(FileLockPath.Normalize(path), value);
                }
                finally
                {
                    _ = CloseHandle(dup);
                }
            }
        }
    }

    private static Builder GetOrAdd(Dictionary<int, Builder> rows, int pid, string? name, string? service)
    {
        if (!rows.TryGetValue(pid, out var row))
        {
            var image = ImagePath(pid);
            row = new Builder(
                pid,
                Name: ProcessName(pid, name, image),
                ImagePath: image,
                ServiceName: string.IsNullOrWhiteSpace(service) ? null : service);
            rows[pid] = row;
        }

        return row;
    }

    private static string ProcessName(int pid, string? preferred, string? image)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && !preferred.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return preferred;
        }

        if (!string.IsNullOrWhiteSpace(image))
        {
            return Path.GetFileName(image);
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        try
        {
            return Process.GetProcessById(pid).ProcessName + ".exe";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return pid == 4 ? "System" : pid.ToString();
        }
    }

    private static string? ImagePath(int pid)
    {
        using var process = OpenProcess(QueryAccess, false, pid);
        if (process.IsInvalid)
        {
            return null;
        }

        var buffer = new StringBuilder(32768);
        var size = buffer.Capacity;
        return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    private static ushort? FileTypeIndex(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[] snapshot)
    {
        var self = Environment.ProcessId;
        foreach (var entry in snapshot)
        {
            if ((int)entry.UniqueProcessId != self)
            {
                continue;
            }

            if (NtDuplicateObject(
                    GetCurrentProcess(),
                    entry.HandleValue,
                    GetCurrentProcess(),
                    out var dup,
                    0,
                    0,
                    DuplicateSameAccess) != 0)
            {
                continue;
            }

            try
            {
                if (GetFileType(dup) == FileTypeDisk)
                {
                    return entry.ObjectTypeIndex;
                }
            }
            finally
            {
                _ = CloseHandle(dup);
            }
        }

        return null;
    }

    private static unsafe SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[] SnapshotHandles()
    {
        var size = 1 << 20;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, size, out var needed);
                if (status == StatusInfoLengthMismatch)
                {
                    size = Math.Max(size * 2, needed + 4096);
                    continue;
                }

                if (status != 0)
                {
                    return [];
                }

                var count = (int)(*(nuint*)buffer);
                if (count <= 0)
                {
                    return [];
                }

                var entries = new SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[count];
                var source = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)(buffer + 2 * sizeof(nint));
                for (var i = 0; i < count; i++)
                {
                    entries[i] = source[i];
                }

                return entries;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return [];
    }

    private static string? PathFromHandle(nint handle)
    {
        var buffer = new char[32768];
        foreach (var flags in new uint[] { 0, FileNameOpened })
        {
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, flags);
            if (length is > 0 and < 32768)
            {
                return new string(buffer, 0, (int)length);
            }
        }

        return null;
    }

    private sealed class Builder(int processId, string Name, string? ImagePath, string? ServiceName)
    {
        private readonly DateTime? _startedAt = StartTime(processId);
        private static DateTime? StartTime(int id)
        {
            try { using var process = Process.GetProcessById(id); return process.StartTime.ToUniversalTime(); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception) { return null; }
        }
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileLockHandle> _handles = [];

        public string Name { get; set; } = Name;

        public string? ServiceName { get; set; } = ServiceName;

        public void Add(string path, nint handle)
        {
            _paths.Add(path);
            _handles.Add(new FileLockHandle(processId, handle, path));
        }

        public FileLockProcess Build() =>
            new(
                processId,
                Name,
                ImagePath,
                ServiceName,
                _startedAt is not null && processId is not (0 or 4) && processId != Environment.ProcessId,
                _paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                _handles) { StartedAtUtc = _startedAt };
    }

    private const int SystemExtendedHandleInformation = 64;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int ErrorMoreData = 234;
    private const uint DuplicateSameAccess = 2;
    private const uint FileTypeDisk = 1;
    private const uint DupAccess = 0x0040 | 0x1000;
    private const uint QueryAccess = 0x1000;
    private const uint FileNameOpened = 8;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, nint info, int length, out int returned);

    [DllImport("ntdll.dll")]
    private static extern int NtDuplicateObject(
        nint sourceProcess,
        nint sourceHandle,
        nint targetProcess,
        out nint targetHandle,
        uint access,
        uint attributes,
        uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        nint handle,
        [Out] char[] buffer,
        uint size,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        int flags,
        StringBuilder name,
        ref int size);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, StringBuilder key);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint session,
        uint fileCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] files,
        uint applicationCount,
        nint applications,
        uint serviceCount,
        nint services);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(
        uint session,
        out uint needed,
        ref uint count,
        [In] [Out] RM_PROCESS_INFO[]? info,
        ref uint rebootReasons);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public nint Object;
        public nint UniqueProcessId;
        public nint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint ProcessId;
        public FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }
}
