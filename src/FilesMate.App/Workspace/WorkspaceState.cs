namespace FilesMate.App.Workspace;

public enum WorkspaceLayoutKind
{
    Single = 0,
    Vertical = 1,
    Horizontal = 2,
}

public sealed record WorkspaceState
{
    public WorkspaceLayoutKind Layout { get; init; } = WorkspaceLayoutKind.Single;

    public double SplitRatio { get; init; } = 0.5;

    public bool IsPreviewVisible { get; init; }

    public string? LeftPath { get; init; }

    public string? RightPath { get; init; }

    public WorkspaceState Normalize()
    {
        var ratio = double.IsFinite(SplitRatio)
            ? Math.Clamp(SplitRatio, 0.2, 0.8)
            : 0.5;
        return this with
        {
            SplitRatio = ratio,
            RightPath = Layout == WorkspaceLayoutKind.Single ? null : RightPath,
        };
    }
}
