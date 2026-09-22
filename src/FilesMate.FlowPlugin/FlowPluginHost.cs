using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using FilesMate.Search;
using System.ComponentModel;

using Microsoft.Data.Sqlite;

namespace FilesMate.FlowPlugin;

public static class FlowPluginHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Handle(string requestJson, string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return SerializeResults([]);
        }

        RpcRequest request;
        try
        {
            request = JsonSerializer.Deserialize<RpcRequest>(requestJson, JsonOptions) ?? new RpcRequest();
        }
        catch (JsonException)
        {
            return SerializeResults([]);
        }

        var method = request.Method ?? string.Empty;
        if (method.Equals("query", StringComparison.OrdinalIgnoreCase))
        {
            var query = FirstString(request.Parameters);
            return SerializeResults(Query(query, databasePath));
        }

        if (method.Equals("context_menu", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeResults(ContextMenu(FirstString(request.Parameters)));
        }

        try
        {
            if (method.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                Open(FirstString(request.Parameters));
                return "{}";
            }
            if (method.Equals("reveal", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(CreateRevealStartInfo(FirstString(request.Parameters), ResolveAppPath()));
                return "{}";
            }
            if (method.Equals("open_filesmate", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo { FileName = ResolveAppPath(), UseShellExecute = true });
                return "{}";
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        {
            return JsonSerializer.Serialize(new { method = "Flow.Launcher.ShowMsg", parameters = new[] { "FilesMate", error.Message, "" } });
        }

        return SerializeResults([]);
    }

    public static IReadOnlyList<FlowResult> Query(string query, string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var db = databasePath ?? ResolveDatabasePath();
        if (string.IsNullOrWhiteSpace(db) || !File.Exists(db))
        {
            return
            [
                new FlowResult
                {
                    Title = "FilesMate 尚未建立搜索索引",
                    SubTitle = "打开 FilesMate，在设置 → 搜索索引中选择目录并建立索引。",
                    JsonRPCAction = new FlowAction { Method = "open_filesmate" },
                    IcoPath = "Images\\app.png",
                    Score = 0,
                },
            ];
        }

        try
        {
            return SearchIndex(db, query);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return
            [
                new FlowResult
                {
                    Title = "FilesMate 搜索暂不可用",
                    SubTitle = ex.Message,
                    IcoPath = "Images\\app.png",
                    Score = 0,
                },
            ];
        }
    }

    public static IReadOnlyList<FlowResult> ContextMenu(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return
        [
            new FlowResult
            {
                Title = "在 FilesMate 中定位", SubTitle = path, IcoPath = "Images\\app.png",
                JsonRPCAction = new FlowAction { Method = "reveal", Parameters = [path] },
            },
            new FlowResult
            {
                Title = "打开", SubTitle = path, IcoPath = "Images\\app.png",
                JsonRPCAction = new FlowAction { Method = "open", Parameters = [path] },
            },
        ];
    }

    public static ProcessStartInfo CreateRevealStartInfo(string path, string executable)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("文件已移动或删除，请刷新 FilesMate 索引。", path);
        var start = new ProcessStartInfo { FileName = executable, UseShellExecute = true };
        start.ArgumentList.Add("/select," + Path.GetFullPath(path));
        return start;
    }

    public static string ResolveAppPath()
    {
        var config = Path.Combine(AppContext.BaseDirectory, "filesmate.json");
        if (File.Exists(config))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            if (document.RootElement.TryGetProperty("executable", out var value)
                && value.GetString() is { } executable && File.Exists(executable)) return executable;
        }
        throw new FileNotFoundException("未找到 FilesMate 程序，请重新运行插件安装脚本。");
    }

    public static void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("文件已移动或删除，请刷新 FilesMate 索引。", path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    public static string ResolveDatabasePath() => GlobalSearchConfiguration.ResolveDatabase();

    public static string ReadRequest(string[] args)
    {
        if (args.Length > 0)
        {
            var joined = string.Join(" ", args).Trim();
            if (joined.StartsWith('{'))
            {
                return joined;
            }
        }

        return Console.IsInputRedirected ? Console.In.ReadToEnd() : string.Empty;
    }

    private static IReadOnlyList<FlowResult> SearchIndex(string databasePath, string query)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        return NameIndexReader.Search(databasePath, query, null, 40, timeout.Token,
                rank: (name, isDirectory) => -SearchScore(name, isDirectory, query))
            .Select(hit => new FlowResult
            {
                Title = hit.Name, SubTitle = hit.Path, IcoPath = hit.Path,
                Score = SearchScore(hit.Name, hit.IsDirectory, query),
                ContextData = hit.Path,
                JsonRPCAction = new FlowAction { Method = "open", Parameters = [hit.Path] },
            }).ToArray();
    }

    private static int SearchScore(string name, bool isDirectory, string query)
    {
        var extension = Path.GetExtension(name);
        var application = !isDirectory && (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".com", StringComparison.OrdinalIgnoreCase));
        // Flow combines scores across plugins. Keep documents below its normal
        // program matches but above the default web fallback (50), while portable
        // executables form a separate top tier (90–120 versus 55–85).
        // Do not assume every .lnk targets an app; Flow's Program plugin resolves those.
        return (application ? 120 : 85) - NameIndexReader.Relevance(name, query) * 10;
    }

    private static string FirstString(JsonElement[]? parameters)
    {
        if (parameters is null || parameters.Length == 0)
        {
            return string.Empty;
        }

        return parameters[0].ValueKind == JsonValueKind.String
            ? parameters[0].GetString() ?? string.Empty
            : parameters[0].ToString();
    }

    private static string SerializeResults(IReadOnlyList<FlowResult> results) =>
        JsonSerializer.Serialize(new RpcResponse { Result = results }, JsonOptions);

    private sealed class RpcRequest
    {
        public string? Method { get; set; }

        public JsonElement[]? Parameters { get; set; }
    }

    private sealed class RpcResponse
    {
        public IReadOnlyList<FlowResult> Result { get; set; } = [];
    }
}

public sealed class FlowResult
{
    public string Title { get; set; } = string.Empty;

    public string SubTitle { get; set; } = string.Empty;

    public string IcoPath { get; set; } = string.Empty;

    public int Score { get; set; }

    public string? ContextData { get; set; }

    public FlowAction? JsonRPCAction { get; set; }
}

public sealed class FlowAction
{
    public string Method { get; set; } = string.Empty;

    public object[] Parameters { get; set; } = [];
}
