namespace FilesMate.Core.Operations;

public static class FileNameRules
{
    public static string Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".."
            || name.EndsWith('.') || name.EndsWith(' ')
            || name.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 || name.Any(char.IsControl))
            throw new IOException("文件名不能包含路径、特殊字符，或以空格和句点结尾。");
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3])))
            throw new IOException("这个名称由 Windows 保留，请使用其他文件名。");
        return name;
    }

    public static int StemLength(string name, bool directory)
    {
        var dot = directory ? -1 : name.LastIndexOf('.');
        return dot > 0 ? dot : name.Length;
    }
}
