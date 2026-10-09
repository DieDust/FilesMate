using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FilesMate.Search;

public sealed record GlobalSearchSettings(bool Enabled = true, string Hotkey = "Alt+Space", string TrayLeftAction = "Search", bool StartAtLogin = true, bool PreviewEnabled = true)
{
    public GlobalSearchSettings Normalize() => this with
    {
        Hotkey = SearchHotkey.TryParse(Hotkey, out var key) ? key.ToString() : "Alt+Space",
        TrayLeftAction = string.Equals(TrayLeftAction, "Files", StringComparison.OrdinalIgnoreCase) ? "Files" : "Search",
    };
}

public readonly record struct SearchHotkey(uint Modifiers, uint Key)
{
    public static bool TryParse(string? text, out SearchHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var flag = parts[i].ToUpperInvariant() switch { "ALT" => 1u, "CTRL" or "CONTROL" => 2u, "SHIFT" => 4u, _ => 0u };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        var keyName = parts[^1].ToUpperInvariant();
        uint key = keyName == "SPACE" ? 32u
            : keyName.Length == 1 && char.IsAsciiLetterOrDigit(keyName[0]) ? keyName[0]
            : keyName.StartsWith('F') && int.TryParse(keyName[1..], out var f) && f is >= 1 and <= 24 ? (uint)(111 + f) : 0;
        if (modifiers == 0 || key == 0 || key == 123) return false; // F12 is reserved for debuggers.
        hotkey = new(modifiers, key);
        return true;
    }
    public override string ToString() =>
        ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "") +
        ((Modifiers & 4) != 0 ? "Shift+" : "") + (Key == 32 ? "Space" : Key >= 112 ? "F" + (Key - 111) : ((char)Key).ToString());
}

public static class GlobalSearchConfiguration
{
    public static string DefaultDirectory => FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath;
    public static string SettingsPath(string? directory = null) => Path.Combine(directory ?? DefaultDirectory, "global-search.json");
    public static GlobalSearchSettings Load(string? directory = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<GlobalSearchSettings>(File.ReadAllText(SettingsPath(directory)),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return PortableSettings(settings?.Normalize() ?? new());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return PortableSettings(new()); }
    }
    private static GlobalSearchSettings PortableSettings(GlobalSearchSettings settings) =>
        FilesMate.Core.Configuration.RuntimeProfile.IsPortable ? settings with { StartAtLogin = false } : settings;
    public static void Save(GlobalSearchSettings settings, string? directory = null)
    {
        if (!SearchHotkey.TryParse(settings.Hotkey, out var key)) throw new ArgumentException("Use a modifier and Space, a letter, digit, or function key.");
        var path = SettingsPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(PortableSettings(settings.Normalize())));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string ResolveDatabase(string? directory = null)
    {
        var root = directory ?? DefaultDirectory;
        if (FilesMate.Core.Configuration.RuntimeProfile.HasPrivateProfile)
            return Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "search-index.db");
        try
        {
            var document = SearchIndexConfigurationFile.Read(Path.Combine(root, "search-index.json"));
            if (SearchIndexConfigurationFile.GetProperty(document, "databaseDirectory") is JsonValue location &&
                location.TryGetValue<string>(out var folder) && !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder))
                return Path.Combine(Path.GetFullPath(folder), "search-index.db");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        return Path.Combine(root, "search-index.db");
    }
    public static string PipeName(string? directory = null)
    {
        var identity = Path.GetFullPath(directory ?? DefaultDirectory).ToUpperInvariant();
        return "FilesMate.Search." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }
}

public sealed record SearchHostRequest(string Command, GlobalSearchSettings? Settings = null, string? ManagerPath = null,
    SearchAppearancePreview? AppearancePreview = null, int PreviewOwnerPid = 0,
    string? PreviewSessionId = null, long PreviewEpoch = 0, long PreviewOwnerStartedUtcTicks = 0, string? PreviewOperation = null);
public sealed record SearchHostReply(bool Ok, string Message, bool HotkeyRegistered = false, bool Visible = false, int Pid = 0, string? ErrorCode = null)
{
    // Set only by the client from the connected native pipe peer; never accepted from wire JSON.
    [System.Text.Json.Serialization.JsonIgnore]
    public long VerifiedServerStartedUtcTicks { get; init; }

    // Older resident hosts omit ErrorCode. Keep their conflict response actionable after upgrade.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHotkeyConflict => !Ok && (ErrorCode == "HotkeyConflict" || ErrorCode is null &&
        Message.Contains("快捷键", StringComparison.Ordinal) && Message.Contains("占用", StringComparison.Ordinal));
}

public static class SearchHostClient
{
    /// <summary>
    /// Whether a resident host for this profile is alive. The host holds this mutex for its whole lifetime, so the
    /// answer is instant; a pipe connect to an absent host would only give up after its two-second timeout.
    /// </summary>
    public static bool IsRunning(string? directory = null)
    {
        try
        {
            if (!Mutex.TryOpenExisting(@"Local\" + GlobalSearchConfiguration.PipeName(directory), out var mutex)) return false;
            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException) { return true; }
        catch (Exception e) when (e is IOException or WaitHandleCannotBeOpenedException) { return false; }
    }

    /// <summary>Asks a running host for its status; <c>null</c> when no host is alive or it did not answer.</summary>
    public static Task<SearchHostReply?> StatusAsync(string? directory = null, CancellationToken token = default) =>
        IsRunning(directory) ? SendAsync(new("status"), directory, token) : Task.FromResult<SearchHostReply?>(null);

    public static async Task<SearchHostReply?> SendAsync(SearchHostRequest request, string? directory = null, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var message = JsonSerializer.Serialize(request);
            if (message.Length > SearchHostProtocol.MaximumRequestCharacters) return null;
            await using var pipe = new NamedPipeClientStream(".", GlobalSearchConfiguration.PipeName(directory), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var server = OperatingSystem.IsWindows() ? OpenPipeServer(pipe.SafePipeHandle) : null;
            if (OperatingSystem.IsWindows() && server is null) return null;
            var serverStartedTicks = server?.StartTime.ToUniversalTime().Ticks ?? 0;
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(message.AsMemory(), timeout.Token).ConfigureAwait(false);
            var response = await SearchHostProtocol.ReadAsync(reader, SearchHostProtocol.MaximumReplyCharacters, timeout.Token).ConfigureAwait(false);
            var reply = response is null ? null : JsonSerializer.Deserialize<SearchHostReply>(response);
            if (server is not null && (server.HasExited || reply?.Pid != server.Id)) return null;
            return reply is null ? null : reply with { VerifiedServerStartedUtcTicks = serverStartedTicks };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException
            or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception) { return null; }
    }

    private static Process? OpenPipeServer(SafePipeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var pid) || pid == 0 || pid > int.MaxValue) return null;
        return Process.GetProcessById((int)pid);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
