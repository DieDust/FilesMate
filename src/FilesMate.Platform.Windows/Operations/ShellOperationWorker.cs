namespace FilesMate.Platform.Windows.Operations;

/// <summary>Runs a shell operation off the UI thread on a short-lived STA.</summary>
public static class ShellOperationWorker
{
    public static Task RunAsync(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { operation(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "FilesMate file operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
