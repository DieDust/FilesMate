namespace FilesMate.Core.Operations;

/// <summary>A completed deletion reported by the shell, not inferred from a missing source.</summary>
public sealed record RecycleItemResult(string OriginalPath, bool IsRecycled);
