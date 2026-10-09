namespace FilesMate.Core.Operations;

/// <summary>A bounded view delta in the same chronological history as file operations.
/// Apply validates its owner and target before mutation; failures retain this history boundary.</summary>
public interface IViewUndoChange
{
    public string Description { get; }
    public void Apply(bool redo);
}
