using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Archives;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FilesMate.App.Views;

internal static class ArchiveOperationUI
{
    public static async Task<FileTransferResult?> RunAsync(FrameworkElement host, CompactMateVerb verb,
        IReadOnlyList<string> paths, string? currentFolder, ILocalFileOperations operations,
        CancellationToken token = default)
    {
        var sources = paths.ToArray();
        if (sources.Length == 0) return null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        void Unloaded(object sender, RoutedEventArgs args) => cancellation.Cancel();
        host.Unloaded += Unloaded;
        try
        {
            // Discovery and shell launch can involve registry, disk and antivirus I/O.
            var launched = false;
            await ShellOperationWorker.RunAsync(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                launched = CompactMateSession.TryLaunch(verb, sources, new CurrentUserRegistry());
            });
            if (launched)
                return null;
            cancellation.Token.ThrowIfCancellationRequested();
            return await RunBuiltInAsync(host, verb, sources, currentFolder, operations, cancellation.Token);
        }
        finally { host.Unloaded -= Unloaded; }
    }

    internal static async Task<FileTransferResult?> RunBuiltInAsync(FrameworkElement host, CompactMateVerb verb,
        IReadOnlyList<string> sources, string? currentFolder, ILocalFileOperations operations,
        CancellationToken token = default)
    {
        if (sources.Count == 0) return null;
        if (verb is CompactMateVerb.Compress7z or CompactMateVerb.Open)
            throw new IOException(StringTable.Get("Archive_ExternalOnly"));
        var compress = verb is CompactMateVerb.CompressZip or CompactMateVerb.CompressNew;
        if (!compress && sources.Any(path => !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
            throw new IOException(StringTable.Get("Archive_Unsupported"));
        var destination = currentFolder ?? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sources[0]));
        if (string.IsNullOrEmpty(destination)) throw new IOException(StringTable.Get("Error_NoFolder"));
        using var lifetime = FileOperationLifetime.Begin();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        void Unloaded(object sender, RoutedEventArgs args) => cancellation.Cancel();
        host.Unloaded += Unloaded;
        try
        {
            var archiveName = sources.Count == 1
                ? Path.GetFileName(Path.TrimEndingDirectorySeparator(sources[0]))
                : StringTable.Get("Archive_DefaultName");
            if (compress && sources.Count == 1 && !await Task.Run(() => Directory.Exists(sources[0]), cancellation.Token))
            {
                var stem = Path.GetFileNameWithoutExtension(archiveName);
                if (!string.IsNullOrEmpty(stem)) archiveName = stem;
            }
            archiveName += ".zip";
            if (verb == CompactMateVerb.CompressNew)
            {
                var choice = await ChooseArchiveAsync(host, archiveName, destination, cancellation.Token);
                if (choice is null) return null;
                (archiveName, destination) = choice.Value;
            }
            else if (verb == CompactMateVerb.ExtractToOther)
            {
                destination = await PickFolderAsync(host);
                if (destination is null) return null;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            var progressDialog = new ArchiveProgressDialog(host, compress, cancellation);
            try
            {
                await progressDialog.ShowAsync();
                return await BuiltInArchiveTransfer.RunAsync(operations, sources, destination, compress,
                    archiveName: archiveName, createSubfolder: verb == CompactMateVerb.ExtractToFolder,
                    resolveConflict: progressDialog.ResolveAsync,
                    progress: new Progress<ArchiveProgress>(progressDialog.Report), token: cancellation.Token,
                    smartExtract: verb == CompactMateVerb.SmartExtract);
            }
            catch (ArchiveOperationException error)
            {
                throw new IOException(StringTable.Get("Archive_Error" + error.ErrorCode), error);
            }
            finally { await progressDialog.HideAsync(); }
        }
        finally { host.Unloaded -= Unloaded; }
    }

    internal static ShelfTransferResult AsShelfResult(FileTransferResult result) =>
        new(result.Completed, result.Errors, result.Cancelled)
        { Skipped = result.Skipped, Undo = result.Undo, WithoutUndo = result.WithoutUndo,
            RemovedSourceDirectories = result.RemovedSourceDirectories };

    private static async Task<string?> PickFolderAsync(FrameworkElement host)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, App.WindowForElement(host)!.NativeHandle);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    private static async Task<(string Name, string Destination)?> ChooseArchiveAsync(
        FrameworkElement host, string archiveName, string destination, CancellationToken token)
    {
        var name = new TextBox { Header = StringTable.Get("Archive_NameLabel"), Text = archiveName, MaxLength = 255 };
        var location = new TextBlock { Text = destination, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        var browse = new Button { Content = StringTable.Get("Shelf_Browse"), HorizontalAlignment = HorizontalAlignment.Left };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, MaxWidth = 420 };
        var content = new StackPanel { Spacing = 12, MinWidth = 300, MaxWidth = 440 };
        content.Children.Add(name);
        content.Children.Add(new TextBlock { Text = StringTable.Get("Archive_OutputLabel") });
        content.Children.Add(location);
        content.Children.Add(browse);
        content.Children.Add(new TextBlock { Text = StringTable.Get("Archive_BuiltinHint"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8 });
        content.Children.Add(error);
        var dialog = new ContentDialog { Title = StringTable.Get("Archive_CreateZip"), Content = content,
            PrimaryButtonText = StringTable.Get("Confirm"), CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary, XamlRoot = host.XamlRoot };
        ContentDialogTheme.Apply(dialog, host);
        browse.Click += async (_, _) =>
        {
            browse.IsEnabled = dialog.IsPrimaryButtonEnabled = false;
            try
            {
                var selected = await PickFolderAsync(host);
                if (selected is not null) location.Text = destination = selected;
            }
            catch (Exception failure) { error.Text = failure.Message; error.Visibility = Visibility.Visible; }
            finally { browse.IsEnabled = dialog.IsPrimaryButtonEnabled = true; }
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var value = name.Text.Trim();
                if (!value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) value += ".zip";
                if (value.Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new IOException();
                archiveName = FileNameRules.Validate(value);
            }
            catch (IOException) { args.Cancel = true; error.Text = StringTable.Get("Archive_NameInvalid"); error.Visibility = Visibility.Visible; }
        };
        using var registration = token.Register(() => host.DispatcherQueue.TryEnqueue(dialog.Hide));
        token.ThrowIfCancellationRequested();
        var result = await dialog.ShowAsync();
        token.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary ? (archiveName, destination) : null;
    }

    private sealed class ArchiveProgressDialog
    {
        private readonly FrameworkElement _host;
        private readonly CancellationTokenSource _cancellation;
        private readonly ContentDialog _dialog;
        private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, IsIndeterminate = true };
        private readonly TextBlock _current = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
        private Task<ContentDialogResult>? _shown;
        private bool _hiding;
        private bool _finished;

        public ArchiveProgressDialog(FrameworkElement host, bool compress, CancellationTokenSource cancellation)
        {
            _host = host; _cancellation = cancellation;
            var content = new StackPanel { Spacing = 12, MinWidth = 300 };
            content.Children.Add(_current); content.Children.Add(_bar); content.Children.Add(_summary);
            _dialog = new ContentDialog { Title = StringTable.Get(compress ? "Archive_TitleCompress" : "Archive_TitleExtract"),
                Content = content, CloseButtonText = StringTable.Get("Archive_Cancel"), XamlRoot = host.XamlRoot };
            ContentDialogTheme.Apply(_dialog, host);
            _dialog.Closing += (_, args) =>
            {
                if (_hiding) return;
                args.Cancel = true;
                _cancellation.Cancel();
                _dialog.CloseButtonText = StringTable.Get("Archive_Cancelling");
            };
        }

        public async Task ShowAsync()
        {
            if (_shown is null && _host.IsLoaded && !_cancellation.IsCancellationRequested)
            {
                var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) => opened.TrySetResult();
                _dialog.Opened += Opened;
                using var registration = _cancellation.Token.Register(() => opened.TrySetCanceled(_cancellation.Token));
                try
                {
                    _shown = _dialog.ShowAsync().AsTask();
                    _ = ObserveAsync(_shown);
                    if (await Task.WhenAny(opened.Task, _shown) == _shown) await _shown;
                    else await opened.Task;
                }
                finally { _dialog.Opened -= Opened; }
            }
            _cancellation.Token.ThrowIfCancellationRequested();

            async Task ObserveAsync(Task task)
            {
                try { await task; }
                catch { _cancellation.Cancel(); }
            }
        }

        public void Report(ArchiveProgress value)
        {
            if (_finished) return;
            _current.Text = Path.GetFileName(value.CurrentPath);
            _bar.IsIndeterminate = value.TotalBytes <= 0;
            if (value.TotalBytes > 0) _bar.Value = Math.Clamp(100d * value.ProcessedBytes / value.TotalBytes, 0, 100);
            _summary.Text = StringTable.Format("Archive_Progress", value.CompletedEntries, value.TotalEntries,
                (value.ProcessedBytes / 1048576d).ToString("N1"), (value.TotalBytes / 1048576d).ToString("N1"));
        }

        public async Task<FileConflictChoice> ResolveAsync(FileConflict conflict, CancellationToken token)
        {
            var completion = new TaskCompletionSource<FileConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => completion.TrySetResult(new(FileConflictAction.Cancel)));
            if (!_host.DispatcherQueue.TryEnqueue(async () =>
            {
                if (completion.Task.IsCompleted) return;
                try
                {
                    await PauseAsync();
                    var choice = await FileConflictDialog.For(_host)(conflict, token);
                    if (choice.Action != FileConflictAction.Cancel && !token.IsCancellationRequested && _host.IsLoaded)
                        await ShowAsync();
                    completion.TrySetResult(choice);
                }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetResult(new(FileConflictAction.Cancel));
            return await completion.Task;
        }

        private async Task PauseAsync()
        {
            if (_shown is null) return;
            _hiding = true;
            try { _dialog.Hide(); await _shown; }
            finally { _shown = null; _hiding = false; }
        }

        public async Task HideAsync() { _finished = true; await PauseAsync(); }
    }
}
