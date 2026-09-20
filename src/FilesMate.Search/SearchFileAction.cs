using System.Text.Json;

namespace FilesMate.Search;

public sealed record SearchFileAction(string Command, string[] Paths)
{
    private const int MaximumRequestBytes = 4 * 1024 * 1024;
    public static bool Supports(string command) => command is "Rename" or "BatchRename" or "Share" or "Recycle"
        or "OpenWith" or "CreateShortcut" or "AddTags" or "AddToShelf" or "AddToFavorites" or "CompressZip" or "Compress7z"
        or "CompressNew" or "ExtractHere" or "ExtractToFolder" or "ExtractToOther" or "SmartExtract"
        or "OpenInCompactMate" or "OpenInTerminal" or "WhoLocks" or "ShowMore";

    private static string RequestPath(string id, string? profile)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("无效的文件操作请求。");
        return Path.Combine(profile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate"), "search-actions", id + ".json");
    }

    public string Write(string? profile = null)
    {
        Validate();
        var data = JsonSerializer.SerializeToUtf8Bytes(this);
        if (data.Length > MaximumRequestBytes) throw new ArgumentException("文件操作请求过大，请减少选择的项目。");
        var id = Guid.NewGuid().ToString("N");
        var path = RequestPath(id, profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(data);
        return id;
    }

    public static SearchFileAction Take(string id, string? profile = null)
    {
        var path = RequestPath(id, profile);
        // Hold exclusive ownership while validating and consuming this one-use request.
        // DeleteOnClose removes it before another process can execute the same action.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
            4096, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        if (stream.Length > MaximumRequestBytes || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(5))
            throw new IOException("文件操作请求已过期，请重新选择。");
        var request = JsonSerializer.Deserialize<SearchFileAction>(stream) ?? throw new IOException("无效的文件操作请求。");
        request.Validate();
        return request;
    }

    private void Validate()
    {
        if (!Supports(Command) || Paths is not { Length: > 0 and <= 512 } ||
            Paths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
            throw new ArgumentException("无效的文件操作或选择内容。");
    }
}
