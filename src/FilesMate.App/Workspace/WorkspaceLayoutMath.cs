namespace FilesMate.App.Workspace;

public static class WorkspaceLayoutMath
{
    public static double ClampRatio(double ratio) =>
        double.IsFinite(ratio) ? Math.Clamp(ratio, 0.2, 0.8) : 0.5;

    public static bool IsSecondaryVisible(WorkspaceLayoutKind layout) =>
        layout != WorkspaceLayoutKind.Single;
}
