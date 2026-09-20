using Loc = FilesMate.App.Localization.StringTable;
using System.Text;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;

namespace FilesMate.App.Views;

internal sealed partial class PaneFileActions
{
    internal static bool IsBusy => _fileWorkActive;
    public async Task<string?> RenamePathAsync(string source, string name)
    {
        if (_fileWorkActive) { _reportError(Loc.Get("Files_Busy")); return null; }
        _fileWorkActive = true;
        using var lifetime = FileOperationLifetime.Begin();
        try { return await RenameCoreAsync(source, name); }
        finally { _fileWorkActive = false; }
    }

    private async Task<string?> RenameCoreAsync(string source, string name)
    {
        try
        {
            FileNameRules.Validate(name);
            var parent = Path.GetDirectoryName(source) ?? throw new IOException(Loc.Get("Files_ParentUnknown"));
            var destination = Path.Combine(parent, name);
            if (string.Equals(source, destination, StringComparison.Ordinal)) return source;
            var samePath = string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
            if (!samePath && Path.Exists(destination)) return await ResolveRenameConflictAsync(source, destination);
            try { await Task.Run(() => _operations.Rename(source, destination)); }
            catch (IOException) when (!samePath && Path.Exists(destination))
            { return await ResolveRenameConflictAsync(source, destination); }
            App.FileUndo.Push(FileUndoRecord.Relocated([new(source, destination)]));
            _refresh();
            return destination;
        }
        catch (Exception error)
        {
            _reportError(error.Message);
            await ShowRenameMessageAsync(Loc.Get("Command_Rename"), error.Message);
            return null;
        }
    }

    private async Task<string?> ResolveRenameConflictAsync(string source, string destination)
    {
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, destination)], move: true,
            FileConflictDialog.For(_host));
        if (result.WithoutUndo > 0) App.FileUndo.Clear();
        if (result.Undo is not null) App.FileUndo.Push(result.Undo);
        if (_host is NavigatorPage page) page.ShowTransferFeedback(new(result.Completed, result.Errors, result.Cancelled)
        { Undo = result.Undo, Skipped = result.Skipped, WithoutUndo = result.WithoutUndo });
        _refresh();
        if (result.Errors.Count > 0)
            await ShowRenameMessageAsync(Loc.Get("Command_Rename"), string.Join(Environment.NewLine, result.Errors));
        if (Path.Exists(source)) return null;
        return result.Completed.Where(pair => string.Equals(pair.Source, source, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Destination).FirstOrDefault() ?? destination;
    }

    private async Task ShowRenameMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog { Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = Loc.Get("Close"), XamlRoot = _host.XamlRoot };
        ContentDialogTheme.Apply(dialog, _host);
        await dialog.ShowAsync();
    }

    private async Task<string?> RequestNameAsync(string title, string folder, string suggestion, string? extension = null)
    {
        var box = new TextBox { Text = suggestion, PlaceholderText = Loc.Get("Sort_Name") };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = folder, TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        body.Children.Add(box);
        body.Children.Add(error);
        var dialog = new ContentDialog { Title = title, Content = body, PrimaryButtonText = Loc.Get("Confirm"), CloseButtonText = Loc.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary, XamlRoot = _host.XamlRoot };
        ContentDialogTheme.Apply(dialog, _host);
        dialog.Opened += (_, _) => { box.Focus(FocusState.Programmatic); box.Select(0, FileNameRules.StemLength(suggestion, extension is null)); };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                FileNameRules.Validate(box.Text);
                if (extension is not null && !box.Text.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(Loc.Format("Files_KeepExtension", extension));
                if (Path.Exists(Path.Combine(folder, box.Text))) throw new IOException(Loc.Get("Files_NameExists"));
            }
            catch (IOException ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; args.Cancel = true; }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    private async Task PasteContentAsync(string folder, DataPackageView data)
    {
        var bitmap = data.Contains(StandardDataFormats.Bitmap);
        if (!bitmap && !data.Contains(StandardDataFormats.Text)) return;
        var extension = bitmap ? ".png" : ".txt";
        var suggested = Path.GetFileName(UniquePath.CombineAvailable(folder,
            (bitmap ? Loc.Get("Clipboard_ImageName") : Loc.Get("Clipboard_TextName")) + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + extension, Path.Exists));
        var name = await RequestNameAsync(bitmap ? Loc.Get("Clipboard_SaveImage") : Loc.Get("Clipboard_SaveText"), folder, suggested, extension);
        if (name is null) return;
        var path = Path.Combine(folder, name);
        await WriteClipboardContentAsync(data, path, bitmap);
        RecordCreated([path]);
        _refresh();
    }

    internal static async Task WriteClipboardContentAsync(DataPackageView data, string path, bool bitmap)
    {
        FileStream? file = null;
        var completed = false;
        try
        {
            if (bitmap)
            {
                using var input = await (await data.GetBitmapAsync()).OpenReadAsync();
                if (input.Size > 128 * 1024 * 1024) throw new IOException(Loc.Get("Clipboard_ImageTooLarge"));
                var decoder = await BitmapDecoder.CreateAsync(input);
                if ((ulong)decoder.PixelWidth * decoder.PixelHeight > 32_000_000) throw new IOException(Loc.Get("Clipboard_DimensionsTooLarge"));
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                    new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
                file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true);
                using var output = file.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, decoder.OrientedPixelWidth,
                    decoder.OrientedPixelHeight, decoder.DpiX, decoder.DpiY, pixels.DetachPixelData());
                await encoder.FlushAsync();
            }
            else
            {
                var text = await data.GetTextAsync();
                if (text.Length > 16_000_000) throw new IOException(Loc.Get("Clipboard_TextTooLong"));
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                await using var writer = new StreamWriter(file, new UTF8Encoding(false), 65536, leaveOpen: true);
                await writer.WriteAsync(text);
                await writer.FlushAsync();
            }
            completed = true;
        }
        finally
        {
            if (file is not null)
            {
                await file.DisposeAsync();
                if (!completed) File.Delete(path); // Only the new, exclusively-created output.
            }
        }
    }

    private async Task GroupSelectionAsync()
    {
        var folder = RequireFolder();
        var selected = _selectedPaths().ToArray();
        if (selected.Length == 0) return;
        var suggestion = Path.GetFileName(UniquePath.CombineAvailable(folder, Loc.Get("Command_NewFolder"), Path.Exists));
        var name = await RequestNameAsync(Loc.Format("Files_GroupSelection", selected.Length), folder, suggestion);
        if (name is null) return;
        var target = Path.Combine(folder, name);
        if (Path.Exists(target)) throw new IOException(Loc.Get("Files_TargetExists"));
        _operations.CreateDirectory(target, failIfExists: true);
        var result = await FileShelfTransfer.RunAsync(_operations, selected, target, move: true, resolveConflict: FileConflictDialog.For(_host));
        if (result.WithoutUndo > 0) App.FileUndo.Clear();
        else if (result.Completed.Count > 0) App.FileUndo.Push(FileUndoRecord.Grouped(target, result.Completed));
        else RecordCreated([target]);
        if (_host is NavigatorPage page) page.ShowTransferFeedback(result);
        if (TransferFeedback.NeedsAttention(result)) _reportError(TransferFeedback.Format(result));
        _refresh();
    }
}
