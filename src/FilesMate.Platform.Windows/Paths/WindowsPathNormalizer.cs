using System.IO;
using System.Text;

namespace FilesMate.Platform.Windows.Paths;

/// <summary>
/// Normalizes Windows paths for navigation without changing the user-visible casing.
/// Comparison uses ordinal ignore-case, matching NTFS default volume behavior.
/// </summary>
public sealed class WindowsPathNormalizer
{
    public string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is empty.", nameof(path));
        }

        if (path.Contains('\0'))
        {
            throw new ArgumentException("Path contains a null character.", nameof(path));
        }

        var text = ExpandUserAndEnvironment(path.Trim().Trim('"')).Replace('/', '\\');
        if (!IsAbsolute(text))
        {
            throw new ArgumentException($"Path must be absolute: '{path}'.", nameof(path));
        }

        var (prefix, root, remainder) = SplitRoot(text);
        var segments = new List<string>();
        foreach (var segment in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        var builder = new StringBuilder();
        builder.Append(prefix);
        builder.Append(root);
        for (var i = 0; i < segments.Count; i++)
        {
            if (builder.Length > 0 && builder[^1] != '\\')
            {
                builder.Append('\\');
            }

            builder.Append(segments[i]);
        }

        var result = builder.ToString();
        return result.Length == 0 ? root : result;
    }

    public bool Equals(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public string? GetParent(string path)
    {
        var normalized = Normalize(path);
        var (prefix, root, remainder) = SplitRoot(normalized);
        if (string.IsNullOrEmpty(remainder))
        {
            return null;
        }

        var last = remainder.LastIndexOf('\\');
        if (last < 0)
        {
            return prefix + root;
        }

        return Normalize(prefix + root + remainder[..last]);
    }

    public string Combine(string directory, string name)
    {
        var root = Normalize(directory);
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Entry name is empty.", nameof(name));
        }

        return root.EndsWith('\\') ? root + name : root + '\\' + name;
    }

    private static string ExpandUserAndEnvironment(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path).Trim();
        if (expanded.Length == 0 || expanded[0] != '~')
        {
            return expanded;
        }

        if (expanded.Length > 1 && expanded[1] is not '\\' and not '/')
        {
            return expanded;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return expanded;
        }

        var rest = expanded.Length <= 2 ? string.Empty : expanded[2..];
        return string.IsNullOrEmpty(rest) ? home : Path.Combine(home, rest.Replace('/', '\\'));
    }

    private static bool IsAbsolute(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
    }

    private static (string Prefix, string Root, string Remainder) SplitRoot(string path)
    {
        var prefix = string.Empty;
        var rest = path;

        if (rest.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            prefix = @"\\?\UNC\";
            rest = rest[8..];
            return SplitUnc(prefix, rest, keepLeadingSlashes: false);
        }

        if (rest.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            prefix = @"\\?\";
            rest = rest[4..];
            if (rest.Length >= 2 && char.IsLetter(rest[0]) && rest[1] == ':')
            {
                return SplitDrive(prefix, rest);
            }

            throw new ArgumentException($"Unsupported long path '{path}'.", nameof(path));
        }

        if (rest.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return SplitUnc(string.Empty, rest[2..], keepLeadingSlashes: true);
        }

        return SplitDrive(string.Empty, rest);
    }

    private static (string Prefix, string Root, string Remainder) SplitDrive(string prefix, string rest)
    {
        if (rest.Length < 2 || rest[1] != ':')
        {
            throw new ArgumentException("Drive path is invalid.", nameof(rest));
        }

        var letter = rest[0];
        var remainder = rest.Length > 2 ? rest[2..].TrimStart('\\') : string.Empty;
        return (prefix, $"{letter}:\\", remainder);
    }

    private static (string Prefix, string Root, string Remainder) SplitUnc(string prefix, string rest, bool keepLeadingSlashes)
    {
        var trimmed = rest.TrimStart('\\');
        var parts = trimmed.Split('\\', 3, StringSplitOptions.None);
        if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
        {
            throw new ArgumentException("UNC path must include server and share.");
        }

        var root = keepLeadingSlashes
            ? $@"\\{parts[0]}\{parts[1]}"
            : $@"{parts[0]}\{parts[1]}";
        var remainder = parts.Length == 3 ? parts[2] : string.Empty;
        return (prefix, root, remainder);
    }
}
