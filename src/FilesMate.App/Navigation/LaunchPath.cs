namespace FilesMate.App.Navigation;

public sealed record LaunchTarget(string? Folder, string? SelectPath, string? SettingsSection = null, bool ActivateOnly = false, string? SearchAction = null)
{
    /// <summary>
    /// A path the caller explicitly asked to open (<c>/open</c>, <c>/select</c>) that no longer exists. Nothing to
    /// navigate to, but the user pressed something and deserves to hear why nothing happened.
    /// </summary>
    public string? MissingPath { get; init; }

    public bool SameDestination(LaunchTarget other) =>
        string.Equals(Folder, other.Folder, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(SelectPath, other.SelectPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(SettingsSection, other.SettingsSection, StringComparison.OrdinalIgnoreCase) && ActivateOnly == other.ActivateOnly && SearchAction == other.SearchAction;
}

public static class LaunchPath
{
    private static readonly string[] ExplorerSwitches = ["/select,", "/e,", "/root,"];
    private const string AppExeName = "FilesMate.App.exe";
    private const string AppDllName = "FilesMate.App.dll";

    public static LaunchTarget Parse(IReadOnlyList<string> args) =>
        ParseNormalized(NormalizeLaunchArgs(args));

    public static string? TryFolder(IReadOnlyList<string> args) => Parse(args).Folder;

    public static IReadOnlyList<string> NormalizeLaunchArgs(IReadOnlyList<string> args)
    {
        if (args is null || args.Count == 0)
        {
            return Environment.GetCommandLineArgs();
        }

        var first = args[0].Trim().Trim('"');
        if (LooksLikeHostBinary(first))
        {
            return args;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            var commandLine = Environment.GetCommandLineArgs();
            exe = commandLine.Length > 0 ? commandLine[0] : AppExeName;
        }

        return new[] { exe }.Concat(args).ToArray();
    }

    public static IReadOnlyList<string> SplitActivationArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        var trimmed = arguments.Trim();
        var unquoted = trimmed.Trim('"');
        if (unquoted.Length > 0 && LooksLikeSingleExistingPath(unquoted))
        {
            return [unquoted];
        }

        return [.. SplitQuoted(trimmed)];
    }

    private static LaunchTarget ParseNormalized(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return new LaunchTarget(null, null);
        }

        var host = args[0];
        for (var i = 1; i < args.Count; i++)
        {
            var raw = CollapseExplorerPrefixes(args[i].Trim().Trim('"'), preserveSelect: true);
            if (IsSwitch(raw, "settings-search")) return new(null, null, "search");
            if (IsSwitch(raw, "search-action") && i + 1 < args.Count && Guid.TryParseExact(args[i + 1], "N", out _))
                return new(null, null, ActivateOnly: true, SearchAction: args[i + 1]);
            if (IsSwitch(raw, "activate")) return new(null, null, ActivateOnly: true);
            if (IsSwitch(raw, "open") || IsSwitch(raw, "directory"))
            {
                if (i + 1 >= args.Count)
                {
                    continue;
                }

                return ExplicitTarget(args[++i].Trim().Trim('"'));
            }

            if (raw.StartsWith("/open,", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("/directory,", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("-directory:", StringComparison.OrdinalIgnoreCase))
            {
                var separator = raw.IndexOfAny([',', ':']);
                var path = separator >= 0 ? raw[(separator + 1)..].Trim().Trim('"') : raw;
                return ExplicitTarget(path);
            }

            if (IsSwitch(raw, "select") && i + 1 < args.Count)
            {
                var selected = ExplicitTarget(args[++i].Trim().Trim('"'), selectDirectory: true);
                if (!IsRedundantHostLaunch(selected, host))
                {
                    return selected;
                }

                continue;
            }

            if (raw.StartsWith("/select,", StringComparison.OrdinalIgnoreCase))
            {
                var path = raw["/select,".Length..].Trim().Trim('"');
                if (path.Length == 0 && i + 1 < args.Count)
                {
                    path = args[++i].Trim().Trim('"');
                }

                var selected = ExplicitTarget(path, selectDirectory: true);
                if (!IsRedundantHostLaunch(selected, host))
                {
                    return selected;
                }
            }
        }

        return TargetFromPlainArgs(args);
    }

    public static bool IsExplorerHost(IReadOnlyList<string> args)
    {
        var normalized = NormalizeLaunchArgs(args);
        if (normalized.Count < 2)
        {
            return false;
        }

        for (var i = 1; i < normalized.Count; i++)
        {
            if (IsExplorerNamespace(Normalize(normalized[i])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExplorerNamespace(string raw) =>
        raw.Contains("20D04FE0-3AEA-1069-A2D8-08002B30309D", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => CollapseExplorerPrefixes(value);

    private static string CollapseExplorerPrefixes(string value, bool preserveSelect = false)
    {
        var raw = value.Trim().Trim('"');
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var prefix in ExplorerSwitches)
            {
                if (preserveSelect && prefix == "/select,")
                {
                    continue;
                }

                if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    raw = raw[prefix.Length..].Trim().Trim('"');
                    changed = true;
                }
            }
        }

        return raw;
    }

    private static bool IsSwitch(string token, string name) =>
        token.Equals("/" + name, StringComparison.OrdinalIgnoreCase)
        || token.Equals("-" + name, StringComparison.OrdinalIgnoreCase)
        || token.Equals("--" + name, StringComparison.OrdinalIgnoreCase);

    private static LaunchTarget TargetFromPlainArgs(IReadOnlyList<string> args)
    {
        var host = args[0];
        for (var i = 1; i < args.Count; i++)
        {
            var raw = Normalize(args[i]);
            if (raw.Length == 0
                || raw.StartsWith('-')
                || (raw.StartsWith('/') && raw.Length < 3)
                || IsDotOrPlaceholder(raw)
                || IsHostArgument(raw, host))
            {
                continue;
            }

            var target = TargetForPath(raw);
            if ((target.Folder is null && target.SelectPath is null)
                || IsRedundantHostLaunch(target, host))
            {
                continue;
            }

            return target;
        }

        return new LaunchTarget(null, null);
    }

    /// <summary>
    /// Like <see cref="TargetForPath"/>, but for a path the caller named explicitly: when it resolves to nothing and
    /// looks like a real file-system path, remember it so the UI can report it instead of silently doing nothing.
    /// </summary>
    private static LaunchTarget ExplicitTarget(string? path, bool selectDirectory = false)
    {
        var target = TargetForPath(path, selectDirectory);
        if (target.Folder is not null || target.SelectPath is not null || string.IsNullOrWhiteSpace(path)) return target;
        var trimmed = path.Trim().Trim('"');
        try
        {
            if (Path.IsPathRooted(trimmed) && !trimmed.StartsWith("::", StringComparison.Ordinal) && !LooksLikeHostBinary(trimmed))
                return target with { MissingPath = Path.GetFullPath(trimmed) };
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException) { }
        return target;
    }

    private static LaunchTarget TargetForPath(string? path, bool selectDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new LaunchTarget(null, null);
        }

        var trimmed = path.Trim().Trim('"');
        if (HomeLocation.IsHome(trimmed) || TagLocation.IsTag(trimmed))
        {
            return new LaunchTarget(ResolveFolder(trimmed), null);
        }
        if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(trimmed, out var device))
            return new LaunchTarget(device.Uri, null);

        var normalized = NormalizeExistingPath(trimmed);
        try
        {
            if (File.Exists(normalized) || (selectDirectory && Directory.Exists(normalized)))
            {
                normalized = Path.TrimEndingDirectorySeparator(normalized!);
                var parent = Path.GetDirectoryName(normalized);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    return new LaunchTarget(Path.GetFullPath(parent), normalized);
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return new LaunchTarget(ResolveFolder(normalized), null);
    }

    private static bool LooksLikeHostBinary(string value)
    {
        var name = TryFileName(value);
        return name.Equals(AppExeName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(AppDllName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHostArgument(string? value, string? host)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value.Trim().Trim('"');
        if (SamePath(raw, host) || SamePath(raw, Environment.ProcessPath))
        {
            return true;
        }

        var rawName = TryFileName(raw);
        if (!LooksLikeHostBinary(rawName) && !LooksLikeHostBinary(raw))
        {
            return false;
        }

        var hostName = TryFileName(host);
        var processName = TryFileName(Environment.ProcessPath);
        return LooksLikeHostBinary(hostName)
            || LooksLikeHostBinary(processName)
            || LooksLikeHostBinary(host ?? string.Empty);
    }

    private static bool IsRedundantHostLaunch(LaunchTarget target, string? host)
    {
        if (target.Folder is null)
        {
            return false;
        }

        if (!IsHostFolder(target.Folder, host))
        {
            return false;
        }

        return target.SelectPath is null || IsHostArgument(target.SelectPath, host);
    }

    private static bool IsHostFolder(string folder, string? host)
    {
        foreach (var candidate in HostFolders(host))
        {
            if (SamePath(folder, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> HostFolders(string? host)
    {
        var fromHost = TryHostDirectory(host);
        if (fromHost is not null)
        {
            yield return fromHost;
        }

        var fromProcess = TryHostDirectory(Environment.ProcessPath);
        if (fromProcess is not null)
        {
            yield return fromProcess;
        }

        var baseDir = AppContext.BaseDirectory?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(baseDir))
        {
            yield return baseDir;
        }
    }

    private static string? TryHostDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var raw = path.Trim().Trim('"');
            if (!LooksLikeHostBinary(raw))
            {
                return null;
            }

            var full = Path.GetFullPath(raw);
            var directory = Path.GetDirectoryName(full);
            return string.IsNullOrEmpty(directory) ? null : directory;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsDotOrPlaceholder(string raw) =>
        raw is "." or "./" or ".\\"
        || raw.Equals("%1", StringComparison.Ordinal);

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim().Trim('"')),
                Path.GetFullPath(right.Trim().Trim('"')),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(
                left.Trim().Trim('"'),
                right.Trim().Trim('"'),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string TryFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileName(path.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeSingleExistingPath(string value)
    {
        if (HomeLocation.IsHome(value) || TagLocation.IsTag(value))
        {
            return true;
        }

        try
        {
            var normalized = NormalizeExistingPath(value);
            return Directory.Exists(normalized) || File.Exists(normalized);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SplitQuoted(string commandLine)
    {
        var current = string.Empty;
        var quoted = false;
        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current;
                    current = string.Empty;
                }

                continue;
            }

            current += ch;
        }

        if (current.Length > 0)
        {
            yield return current;
        }
    }

    private static string? ResolveFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (HomeLocation.IsHome(path))
        {
            return HomeLocation.Uri;
        }

        if (TagLocation.TryParse(path, out var tagId))
        {
            return TagLocation.Uri(tagId);
        }

        var raw = path;
        if (raw.Length == 2 && char.IsAsciiLetter(raw[0]) && raw[1] == ':')
        {
            raw += Path.DirectorySeparatorChar;
        }

        try
        {
            if (Directory.Exists(raw))
            {
                return Path.GetFullPath(raw);
            }

            if (File.Exists(raw))
            {
                var parent = Path.GetDirectoryName(Path.GetFullPath(raw));
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    return parent;
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return null;
    }

    private static string? NormalizeExistingPath(string path)
    {
        try
        {
            return AddressPath.Normalize(path, Environment.CurrentDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }
}
