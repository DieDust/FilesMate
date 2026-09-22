namespace FilesMate.Core.Operations;

public sealed class UndoStateChangedException() : IOException(
    "Undo stopped: an item changed, is inaccessible, or exceeds the safety-check limit. Your files were kept.");
