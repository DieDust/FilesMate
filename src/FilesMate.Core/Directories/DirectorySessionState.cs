namespace FilesMate.Core.Directories;

public enum DirectorySessionState
{
    Loading = 0,
    Partial = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>
/// Coalesced session notification for the UI. Not a per-file event.
/// </summary>
public readonly record struct DirectorySessionChange(int EntryCount, DirectorySessionState State, DirectoryReadError? Error);
