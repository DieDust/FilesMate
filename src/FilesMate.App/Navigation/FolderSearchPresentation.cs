namespace FilesMate.App.Navigation;

public enum SearchSubmitTarget { Unavailable, Folder, Global }

/// <summary>Scope and layout rules for the permanent toolbar search.</summary>
public static class FolderSearchPresentation
{
    public static SearchSubmitTarget SubmitTarget(bool global, bool hasFolder) =>
        global ? SearchSubmitTarget.Global : hasFolder ? SearchSubmitTarget.Folder : SearchSubmitTarget.Unavailable;

    // At narrow widths use another row rather than clipping query/submit/scope.
    public static double ControlWidth(double availableWidth, bool stacked) =>
        Math.Min(Math.Max(0, availableWidth), Math.Clamp(availableWidth * (stacked ? .4 : .2), 220, 240));
}
