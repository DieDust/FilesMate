namespace FilesMate.Core.Operations;

/// <summary>Owns the previous version for the lifetime of this session's undo entry.</summary>
public sealed class FileReplacement(string source, string destination, string backup, bool move, IDisposable? budgetLease = null) : IDisposable
{
    public string Source { get; } = source;
    public string Destination { get; } = destination;
    public string Backup { get; private set; } = backup;
    public bool IsApplied { get; private set; } = true;
    private FileConflictDetails _destinationVersion = FileConflictDetails.Read(destination);
    private FileConflictDetails _backupVersion = FileConflictDetails.Read(backup);

    private FileUndoState _destinationState = FileUndoState.Capture([destination]);
    private FileUndoState _backupState = FileUndoState.Capture([backup]);
    private FileUndoState? _sourceState;

    public void Undo()
    {
        if (!IsApplied) return;
        _destinationState.Validate();
        Validate(Destination, _destinationVersion);
        _backupState.Validate();
        Validate(Backup, _backupVersion);
        if (move)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            File.Move(Destination, Source, overwrite: false);
            try { File.Move(Backup, Destination, overwrite: false); }
            catch { File.Move(Source, Destination, overwrite: false); throw; }
            _sourceState = FileUndoState.Capture([Source]);
            _incomingVersion = FileConflictDetails.Read(Source); // NTFS may restore the old name's creation timestamp.
        }
        else Swap();
        IsApplied = false;
        _destinationVersion = FileConflictDetails.Read(Destination);
        _destinationState = FileUndoState.Capture([Destination]);
    }

    public void Redo()
    {
        if (IsApplied) return;
        _destinationState.Validate();
        Validate(Destination, _destinationVersion);
        if (move)
        {
            // An edited source is not the version whose replacement was approved.
            (_sourceState ?? throw new IOException("Missing replacement state.")).Validate();
            Validate(Source, _incomingVersion);
            File.Move(Destination, Backup, overwrite: false);
            try { File.Move(Source, Destination, overwrite: false); }
            catch { File.Move(Backup, Destination, overwrite: false); throw; }
            _backupVersion = FileConflictDetails.Read(Backup);
            _backupState = FileUndoState.Capture([Backup]);
        }
        else { _backupState.Validate(); Validate(Backup, _backupVersion); Swap(); }
        IsApplied = true;
        _destinationVersion = FileConflictDetails.Read(Destination);
        _destinationState = FileUndoState.Capture([Destination]);
    }

    private FileConflictDetails _incomingVersion = FileConflictDetails.Read(destination);

    private void Swap()
    {
        var nextBackup = Path.Combine(Path.GetDirectoryName(Backup)!, Guid.NewGuid().ToString("N") + Path.GetExtension(Destination));
        File.Replace(Backup, Destination, nextBackup);
        Backup = nextBackup;
        _backupVersion = FileConflictDetails.Read(Backup);
        _backupState = FileUndoState.Capture([Backup]);
    }

    private static void Validate(string path, FileConflictDetails expected)
    {
        if (FileConflictDetails.Read(path) != expected
            || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException($"The file changed since the operation: {path}");
    }

    public void Dispose()
    {
        // After undoing a move, both versions are back at their original paths.
        try
        {
            if ((!move || IsApplied) && File.Exists(Backup))
            {
                _backupState.Validate();
                Validate(Backup, _backupVersion);
                File.Delete(Backup);
            }
            if (Directory.Exists(Path.GetDirectoryName(Backup))) Directory.Delete(Path.GetDirectoryName(Backup)!, recursive: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Keep an inaccessible or externally changed backup for manual recovery.
            System.Diagnostics.Trace.TraceWarning("Replacement backup retained at {0}: {1}", Backup, error.Message);
        }
        finally
        {
            try { budgetLease?.Dispose(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.TraceWarning("Backup journal cleanup failed: {0}", error.Message); }
        }
    }
}
