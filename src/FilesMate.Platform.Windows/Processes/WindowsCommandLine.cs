using System.Text;

namespace FilesMate.Platform.Windows.Processes;

internal static class WindowsCommandLine
{
    internal static string JoinArguments(IReadOnlyList<string> arguments) => string.Join(' ', arguments.Select(Quote));

    internal static string Quote(string value)
    {
        if (value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"')) return value;
        var result = new StringBuilder(value.Length + 2).Append('"');
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
            else result.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
