namespace FilesMate.App.Controls.FileSurface;

/// <summary>A menu selection updates commands and status without starting a preview.</summary>
public sealed class ContextSelectionChangedEventArgs : EventArgs
{
    public static ContextSelectionChangedEventArgs Instance { get; } = new();
    private ContextSelectionChangedEventArgs() { }
}
