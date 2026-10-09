namespace FilesMate.App.Navigation;

/// <summary>Disabled priority, then model-selected contrast, then nonselected interaction ink.</summary>
public static class SidebarInkPolicy
{
    public static string Resource(bool enabled, bool current, bool interacting) => !enabled
        ? "FilesMate.Text.DisabledBrush" : current ? "FilesMate.FileItem.SelectedForegroundBrush"
        : interacting ? "FilesMate.Button.ActiveForegroundBrush" : "FilesMate.Text.PrimaryBrush";
}
