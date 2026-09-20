namespace FilesMate.Platform.Windows.Shell;

public sealed record ShellMenuItem(
    string Label,
    uint CommandId,
    bool IsSeparator,
    bool Enabled,
    IReadOnlyList<ShellMenuItem> Children);
