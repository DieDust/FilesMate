namespace FilesMate.App.Navigation;

/// <summary>Address aliases only; never interprets arbitrary command lines.</summary>
public static class SpecialLocation
{
    public static string? ShellName(string input)
    {
        var text = input.Trim().Trim('"');
        if (text.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.StartsWith("::{", StringComparison.Ordinal) && Guid.TryParse(text[2..], out _)) return text;
        return text.ToLowerInvariant() switch
        {
            "回收站" or "recycle bin" => "shell:RecycleBinFolder",
            "此电脑" or "我的电脑" or "this pc" => "shell:MyComputerFolder",
            "网络" or "network" => "shell:NetworkPlacesFolder",
            "控制面板" or "control panel" => "shell:ControlPanelFolder",
            "库" or "libraries" => "shell:Libraries",
            "桌面" or "desktop" => "shell:Desktop",
            "文档" or "documents" => "shell:Personal",
            "下载" or "downloads" => "shell:Downloads",
            "图片" or "pictures" => "shell:My Pictures",
            "音乐" or "music" => "shell:My Music",
            "视频" or "videos" => "shell:My Video",
            "启动" or "startup" => "shell:Startup",
            "最近使用" or "recent" => "shell:Recent",
            "用户" or "userprofile" => "shell:Profile",
            _ => null,
        };
    }
}
