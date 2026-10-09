namespace FilesMate.App.Controls.Menus;

public sealed record RecursiveContextAction(string Label, string Glyph, Action Invoke,
    IReadOnlyList<RecursiveContextAction>? Children = null);
