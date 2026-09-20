namespace FilesMate.Core.Navigation;

/// <summary>
/// Stable identity for a navigation pane. Thread-safe as a value. Does not own sessions or cancellation.
/// </summary>
public readonly record struct PaneId(Guid Value)
{
    public static PaneId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
