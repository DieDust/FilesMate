using FilesMate.App.Models;

namespace FilesMate.App.Controls.FeatureTour;

internal sealed record FeatureTourChoices
{
    public ItemOpeningMode FileOpening { get; set; }
    public ItemOpeningMode FolderOpening { get; set; }
    public FolderViewKind DefaultView { get; set; }
    public bool RestoreSession { get; set; }
    public bool Favorites { get; set; }
    public bool Alphabet { get; set; }
    public bool Indexing { get; set; }
    public bool Search { get; set; }
    public bool Startup { get; set; }
    public string Hotkey { get; set; } = "Alt+Space";
    public AppThemeKind Theme { get; set; }
    public AccentKind Accent { get; set; }
    public string? CustomAccent { get; set; }

    public bool IsValid => Enum.IsDefined(FileOpening) && Enum.IsDefined(FolderOpening)
        && Enum.IsDefined(DefaultView) && Enum.IsDefined(Theme) && Enum.IsDefined(Accent)
        && Hotkey is { Length: <= 128 };
}
