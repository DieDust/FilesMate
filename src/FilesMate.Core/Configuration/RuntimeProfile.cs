using System.Security.Cryptography;
using System.Text;

namespace FilesMate.Core.Configuration;

/// <summary>Process-local storage for ordinary, portable and explicitly isolated test launches.</summary>
public sealed record ProfileContext(string? IsolatedDirectory, string? PortableDirectory = null)
{
    public const string Argument = "--test-profile";
    public const string MarkerName = ".filesmate-isolated-profile";
    public const string MarkerContent = "FilesMate isolated test profile v1";
    public const string PortableMarkerName = ".filesmate-portable";
    public const string PortableMarkerContent = "FilesMate portable v1";
    public const string PortableDataMarkerName = ".filesmate-profile";
    public const string PortableDataMarkerContent = "FilesMate portable data v1";
    public static string OrdinaryDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate");
    public bool IsIsolated => IsolatedDirectory is not null;
    public bool IsPortable => PortableDirectory is not null;
    public bool HasPrivateProfile => IsIsolated || IsPortable;
    public string DirectoryPath => IsolatedDirectory ?? PortableDirectory ?? OrdinaryDirectory;

    public static ProfileContext Parse(IReadOnlyList<string> args, string? executable = null)
    {
        var portable = PortableDirectoryForExecutable(executable ?? Environment.ProcessPath);
        var indexes = Enumerable.Range(0, args.Count).Where(i => args[i].Equals(Argument, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (indexes.Length == 0) return new(IsolatedDirectory: null, PortableDirectory: portable);
        if (portable is not null) throw new ArgumentException("A portable launch cannot also select a test profile.");
        if (indexes.Length != 1 || indexes[0] + 1 >= args.Count) throw new ArgumentException("--test-profile requires one explicit D profile directory.");
        var raw = args[indexes[0] + 1];
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(raw) || raw.IndexOf(':', 2) >= 0
            || raw.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new ArgumentException("The test profile must be an absolute D directory.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
        if (!path.StartsWith(@"D:\FilesMate\artifacts\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The test profile must be inside D:\\FilesMate\\artifacts.");
        ValidateAncestors(path);
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()
            && (!File.Exists(Path.Combine(path, MarkerName)) || File.ReadAllText(Path.Combine(path, MarkerName)) != MarkerContent))
            throw new ArgumentException("An existing nonempty profile requires its isolated-test marker.");
        return new(path);
    }

    private static string? PortableDirectoryForExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        var name = Path.GetFileName(executable);
        var root = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        if (name.Equals("FilesMate.SearchHost.exe", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(root).Equals("SearchHost", StringComparison.OrdinalIgnoreCase))
            root = Path.GetDirectoryName(root)!;
        else if (!name.Equals("FilesMate.App.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var marker = Path.Combine(root, PortableMarkerName);
        try
        {
            if ((File.GetAttributes(marker) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("The portable marker must be a regular file.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if (File.ReadAllText(marker).Trim() != PortableMarkerContent)
            throw new IOException("The portable marker is invalid. Extract the complete portable ZIP again.");
        var data = Path.Combine(root, "data");
        ValidateAncestors(data);
        return data;
    }

    private static void ValidateAncestors(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current)) throw new ArgumentException("A profile ancestor is a file.");
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("A private profile cannot traverse a reparse point.");
        }
    }

    private static void ValidatePortableOwnership(string data)
    {
        if (!Directory.Exists(data) || !Directory.EnumerateFileSystemEntries(data).Any()) return;
        var owner = Path.Combine(data, PortableDataMarkerName);
        if (!File.Exists(owner) || (File.GetAttributes(owner) & FileAttributes.ReparsePoint) != 0
            || File.ReadAllText(owner) != PortableDataMarkerContent)
            throw new IOException("The portable data folder is not a FilesMate profile. Choose a new extraction folder.");
    }

    public void PreparePortableStorage()
    {
        if (!IsPortable) return;
        using var gate = new Mutex(false, @"Local\FilesMate.PortableProfile." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DirectoryPath.ToUpperInvariant())))[..24]);
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The portable profile is busy. Try launching again.");
            PreparePortableStorageCore();
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }

    private void PreparePortableStorageCore()
    {
        ValidateAncestors(DirectoryPath);
        ValidatePortableOwnership(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        var owner = Path.Combine(DirectoryPath, PortableDataMarkerName);
        try
        {
            using var file = new FileStream(owner, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var bytes = Encoding.UTF8.GetBytes(PortableDataMarkerContent);
            file.Write(bytes);
        }
        catch (IOException) when (File.Exists(owner) && File.ReadAllText(owner) == PortableDataMarkerContent) { }
        // Probe the real data directory; never fall back to the installed profile.
        using var probe = new FileStream(Path.Combine(DirectoryPath, "." + Guid.NewGuid().ToString("N") + ".write-check"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        probe.WriteByte(0);
    }

    public string MapSettingsPath(string path)
    {
        if (!HasPrivateProfile) return path;
        var full = Path.GetFullPath(path);
        if (IsInside(full, DirectoryPath)) return full;
        if (!IsInside(full, OrdinaryDirectory)) throw new ArgumentException("Settings path escapes the private profile.");
        return Path.Combine(DirectoryPath, Path.GetRelativePath(OrdinaryDirectory, full));
    }

    public void RequireInstallerUpdates()
    {
        if (IsPortable) throw new InvalidOperationException("Portable updates require a new ZIP. Close FilesMate, replace the program files and keep the data folder.");
    }

    public static bool IsInside(string path, string root) => string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public string ScopedName(string ordinaryName) => !HasPrivateProfile ? ordinaryName : ordinaryName + (IsPortable ? ".Portable." : ".Test.") +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DirectoryPath.ToUpperInvariant())))[..24];

    public string[] ForChild(IReadOnlyList<string> args) => !IsIsolated ? args.ToArray() :
        [.. WithoutProfileArguments(args), Argument, DirectoryPath];

    public string[] ForExecutable(string executable, IReadOnlyList<string> args)
    {
        var name = Path.GetFileName(executable);
        return IsIsolated && (name.Equals("FilesMate.App.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("FilesMate.SearchHost.exe", StringComparison.OrdinalIgnoreCase))
            ? ForChild(args) : args.ToArray();
    }

    public static IEnumerable<string> WithoutProfileArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(Argument, StringComparison.OrdinalIgnoreCase)) { i++; continue; }
            yield return args[i];
        }
    }
}

public static class RuntimeProfile
{
    private static ProfileContext _context = new(IsolatedDirectory: null);
    public static string? InitializationError { get; set; }
    public static bool IsIsolated => _context.IsIsolated;
    public static bool IsPortable => _context.IsPortable;
    public static bool HasPrivateProfile => _context.HasPrivateProfile;
    public static string DirectoryPath => _context.DirectoryPath;
    public static void Initialize(IReadOnlyList<string> args)
    {
        // Validation completes before any directory, log, database, or environment write.
        var context = ProfileContext.Parse(args);
        context.PreparePortableStorage();
        if (context.HasPrivateProfile)
        {
            Directory.CreateDirectory(context.DirectoryPath);
            if (context.IsIsolated)
                File.WriteAllText(Path.Combine(context.DirectoryPath, ProfileContext.MarkerName), ProfileContext.MarkerContent);
            var temporary = Path.Combine(context.DirectoryPath, "temp");
            Directory.CreateDirectory(temporary);
            Environment.SetEnvironmentVariable("TEMP", temporary);
            Environment.SetEnvironmentVariable("TMP", temporary);
            if (context.IsIsolated)
            {
                foreach (var name in new[] { "FILESMATE_PR00_TRACE", "FILESMATE_PR00_TRACE_FILE", "FILESMATE_PR00_RUN_ID" })
                    Environment.SetEnvironmentVariable(name, null);
                File.WriteAllText(Path.Combine(context.DirectoryPath, "runtime-profile-" + Path.GetFileNameWithoutExtension(Environment.ProcessPath) + ".json"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Pid = Environment.ProcessId, Executable = Environment.ProcessPath, Profile = context.DirectoryPath,
                        Temporary = temporary, MainInstance = context.ScopedName("FilesMate.Main"),
                        IncomingEvent = context.ScopedName(@"Local\FilesMate.IncomingLaunch"), PersistentIntegration = false,
                    }));
            }
        }
        _context = context;
    }
    public static string MapSettingsPath(string path) => _context.MapSettingsPath(path);
    public static string ScopedName(string name) => _context.ScopedName(name);
    public static string[] ForChild(IReadOnlyList<string> args) => _context.ForChild(args);
    public static string[] ForExecutable(string executable, IReadOnlyList<string> args) => _context.ForExecutable(executable, args);
    public static IEnumerable<string> WithoutProfileArguments(IReadOnlyList<string> args) => ProfileContext.WithoutProfileArguments(args);
    public static void RequireInside(string path)
    {
        if (HasPrivateProfile && !ProfileContext.IsInside(Path.GetFullPath(path), DirectoryPath))
            throw new ArgumentException("The destination must remain inside its private profile.");
    }
    public static void RequirePersistentIntegration()
    {
        if (HasPrivateProfile) throw new InvalidOperationException("Windows integration is disabled for portable and isolated test profiles.");
    }
    public static void RequireInstallerUpdates()
    {
        _context.RequireInstallerUpdates();
    }
}
