using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.App.Theming;
using FilesMate.App.Views;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar
{
    public Func<FileDropRequest, Task>? DropRequested { get; set; }
    private FrameworkElement? _crumbDropTarget;

    private void ConfigureCrumbDrop(FrameworkElement target, string path)
    {
        // Home, search and tag breadcrumbs are navigation locations, not folders.
        if (!Path.IsPathFullyQualified(path) && !PortableDeviceLocation.TryParse(path, out _)) return;
        target.AllowDrop = true;
        target.DragOver += Crumb_DragOver;
        target.DragLeave += (_, _) => { if (ReferenceEquals(_crumbDropTarget, target)) SetCrumbDropTarget(null); };
        target.Drop += Crumb_Drop;
        target.Unloaded += (_, _) => { if (ReferenceEquals(_crumbDropTarget, target)) SetCrumbDropTarget(null); };
    }

    private void Crumb_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string destination } target) return;
        var operation = DropRequested is null ? DataPackageOperation.None
            : CrumbFileOperation(e.DataView, destination,
                e.Modifiers.HasFlag(DragDropModifiers.Control), e.Modifiers.HasFlag(DragDropModifiers.Shift));
        e.AcceptedOperation = operation;
        e.Handled = true;
        e.DragUIOverride.IsCaptionVisible = operation != DataPackageOperation.None;
        e.DragUIOverride.IsGlyphVisible = operation == DataPackageOperation.None;
        if (operation != DataPackageOperation.None)
            e.DragUIOverride.Caption = StringTable.Get(operation == DataPackageOperation.Move ? "Drag_MoveTo" : "Drag_CopyTo")
                + " " + LocationCaption.Title(destination);
        SetCrumbDropTarget(operation == DataPackageOperation.None ? null : target);
    }

    private async void Crumb_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string destination }) return;
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.None;
        SetCrumbDropTarget(null);
        var deferral = e.GetDeferral();
        try
        {
            e.AcceptedOperation = await DropFilesOnCrumbAsync(e.DataView, destination,
                e.Modifiers.HasFlag(DragDropModifiers.Control), e.Modifiers.HasFlag(DragDropModifiers.Shift));
        }
        catch (Exception error) { App.LogFailure("BreadcrumbFileDrop", error); }
        finally { deferral.Complete(); }
    }

    private static DataPackageOperation CrumbFileOperation(DataPackageView data, string destination,
        bool control, bool shift, IReadOnlyList<string>? paths = null)
    {
        if (!DeviceTransferUI.HasFiles(data)) return DataPackageOperation.None;
        if (PortableDeviceLocation.TryParse(destination, out _) || data.Contains(DeviceTransferUI.ClipboardFormat))
            return !shift && (Path.IsPathFullyQualified(destination) || PortableDeviceLocation.TryParse(destination, out _))
                ? DataPackageOperation.Copy : DataPackageOperation.None;
        return FileDropPolicy.ResolveOperation(paths ?? FileDropRequest.SourcePaths(data), destination, true, control, shift) switch
        {
            FileDropOperation.Copy => DataPackageOperation.Copy,
            FileDropOperation.Move => DataPackageOperation.Move,
            _ => DataPackageOperation.None,
        };
    }

    private async Task<DataPackageOperation> DropFilesOnCrumbAsync(DataPackageView data, string destination, bool control, bool shift)
    {
        var receive = DropRequested;
        if (receive is null || !IsLoaded || !DeviceTransferUI.HasFiles(data)) return DataPackageOperation.None;
        var paths = await DeviceTransferUI.ReadItemsAsync(data);
        // The destination is captured before reading the payload. A pane switch or
        // path refresh during that await must never redirect the files elsewhere.
        var operation = CrumbFileOperation(data, destination, control, shift, paths);
        if (paths.Length == 0 || operation == DataPackageOperation.None || !IsLoaded || DropRequested is null)
            return DataPackageOperation.None;
        await receive(new FileDropRequest(paths, destination, operation,
            data.Properties.ContainsKey(FileDropRequest.ShelfMarker), control));
        return operation;
    }

    private void SetCrumbDropTarget(FrameworkElement? target)
    {
        if (ReferenceEquals(_crumbDropTarget, target)) return;
        if (_crumbDropTarget is { } previous)
        {
            ThemeResources.Clear(previous, previous is Grid ? Grid.BackgroundProperty : Control.BackgroundProperty);
            ThemeResources.Clear(previous, previous is Grid ? Grid.BorderBrushProperty : Control.BorderBrushProperty);
            if (previous is Grid) ThemeResources.Bind(previous, Grid.BackgroundProperty, "FilesMate.TransparentBrush");
        }
        _crumbDropTarget = target;
        if (target is null) return;
        ThemeResources.Bind(target, target is Grid ? Grid.BackgroundProperty : Control.BackgroundProperty, "FilesMate.Item.DropTargetBrush");
        ThemeResources.Bind(target, target is Grid ? Grid.BorderBrushProperty : Control.BorderBrushProperty, "FilesMate.Selection.AccentBrush");
    }
}
