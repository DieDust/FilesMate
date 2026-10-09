using FilesMate.App.Navigation;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private int _addressSubmissionVersion;
    private CancellationTokenSource? _addressSubmissionRequest;
    private static readonly SemaphoreSlim AddressProbeSlots = new(2, 2);

    private void CancelAddressSubmission()
    {
        ++_addressSubmissionVersion;
        _addressSubmissionRequest?.Cancel();
    }

    private void SupersedePendingAddressInput()
    {
        CancelAddressSubmission();
        Omni.CancelPendingPathPaste();
    }

    private async void Omni_PathSubmitted(object? sender, string path)
    {
        CancelAddressSubmission();
        var version = _addressSubmissionVersion;
        var pane = ViewModel;
        var rootBefore = pane.AddressText;
        var generationBefore = pane.Navigation.CurrentGeneration;
        var inputRevision = Omni.AddressInputRevision;
        var basis = Omni.AddressInputBasePath;
        var allowShellAlias = !Omni.IsPastePathSubmission;
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _addressSubmissionRequest = request;
        bool Current() => !_disposed && version == _addressSubmissionVersion && ReferenceEquals(pane, ViewModel)
            && pane.AddressText == rootBefore && pane.Navigation.CurrentGeneration == generationBefore
            && Omni.AddressInputRevision == inputRevision && Omni.IsEditing && Omni.DraftText == path;
        try
        {
            var normalized = AddressPath.Normalize(path, basis);
            if (HomeLocation.IsHome(normalized) || TagLocation.IsTag(normalized)
                || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(normalized, out _))
            {
                ScheduleNavigation(() =>
                {
                    if (Omni.TryCommitPath(_ => normalized)) pane.NavigateDirectory(normalized);
                }, Current);
                return;
            }
            (string Folder, string? File) target;
            try { target = await ProbeAddressAsync(normalized, request.Token); }
            catch (DirectoryNotFoundException) when (allowShellAlias && SpecialLocation.ShellName(path) is not null)
            {
                var shellName = SpecialLocation.ShellName(path)!;
                var folder = await FilesMate.Platform.Windows.Shell.ShellLocation.ResolveFileSystemPathAsync(shellName).WaitAsync(request.Token);
                if (!Current()) return;
                if (folder is null)
                {
                    await FilesMate.Platform.Windows.Associations.ClassicExplorer.OpenLocationAsync(shellName);
                    if (Current()) Omni.CancelMode();
                    return;
                }
                target = await ProbeAddressAsync(folder, request.Token);
            }
            ScheduleNavigation(() =>
            {
                if (!Omni.TryCommitPath(_ => target.Folder)) return;
                NavigateAddressTarget(pane, target.Folder, target.File);
            }, Current);
        }
        catch (OperationCanceledException)
        {
            if (Current()) Omni.TryCommitPath(_ => throw new IOException(Loc.Get("Address_PathTimeout")));
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (Current()) Omni.TryCommitPath(_ => throw new IOException(Loc.Get(error is ArgumentException or NotSupportedException ? "Address_InvalidPath" : "Address_PathUnavailable")));
        }
        finally
        {
            if (ReferenceEquals(_addressSubmissionRequest, request)) _addressSubmissionRequest = null;
            request.Dispose();
        }
    }

    private void NavigateAddressTarget(PaneViewModel pane, string folder, string? file)
    {
        _pendingCreatedItemRename = false;
        _pendingSelectPath = file;
        _pendingSelectPane = pane;
        _selectAttempts = 0;
        pane.NavigateDirectory(folder);
    }

    private string? SelectedAddressParent(PaneViewModel pane)
    {
        if (!pane.IsRecursive || SurfaceOf(pane).GetSelectedLocation() is not { } selected) return null;
        return new WindowsPathService().GetParent(selected.Path);
    }

    private void NavigateAddressUp()
    {
        var pane = ViewModel;
        var parent = SelectedAddressParent(pane);
        if (parent is null) ScheduleNavigation(pane.Up);
        else ScheduleNavigation(() => pane.NavigateDirectory(parent));
    }

    private static async Task<(string Folder, string? File)> ProbeAddressAsync(string path, CancellationToken token)
    {
        await AddressProbeSlots.WaitAsync(token).ConfigureAwait(false);
        // A filesystem/network probe can outlive cancellation. It keeps its slot
        // until it really finishes, so repeated requests cannot flood workers.
        var work = Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var file = File.Exists(path) ? path : null;
                var folder = file is null ? path : Path.GetDirectoryName(file)!;
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException();
                using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
                _ = entries.MoveNext();
                return (Folder: folder, File: file);
            }
            finally { AddressProbeSlots.Release(); }
        });
        return await work.WaitAsync(token).ConfigureAwait(false);
    }
}
