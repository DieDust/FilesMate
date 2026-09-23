using System.ComponentModel;
using System.Runtime.CompilerServices;

using FilesMate.App.Icons;
using FilesMate.App.Localization;
using FilesMate.App.Theming;
using FilesMate.Platform.Windows.Locks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.Storage.Pickers;

using WinRT.Interop;

namespace FilesMate.App.Views;

public sealed class FileLockNode : INotifyPropertyChanged
{
    private bool _selected;
    private bool _expanded;

    public FileLockNode(FileLockProcess process, string? lockedPath = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        Process = process;
        LockedPath = string.IsNullOrWhiteSpace(lockedPath) ? null : lockedPath;
        if (LockedPath is null)
        {
            Title = process.Name;
            Subtitle = string.IsNullOrWhiteSpace(process.ImagePath)
                ? StringTable.Format("Lock_Pid", process.ProcessId)
                : process.ImagePath;
            IconPath = process.ImagePath;
            IconIsDirectory = false;
            Children = [.. process.Paths.Select(path => new FileLockNode(process, path))];
        }
        else
        {
            Title = LockedPath;
            Subtitle = string.Empty;
            IconPath = LockedPath;
            IconIsDirectory = Directory.Exists(LockedPath);
            Children = [];
        }
    }

    public FileLockProcess Process { get; }

    public string? LockedPath { get; }

    public bool IsProcess => LockedPath is null;

    public string Title { get; }

    public string Subtitle { get; }

    public string? IconPath { get; }

    public bool IconIsDirectory { get; }

    public IReadOnlyList<FileLockNode> Children { get; }

    public bool HasChildren => Children.Count > 0;

    public IReadOnlyList<FileLockHandle> Handles => FileLockQuery.HandlesFor(Process, LockedPath);

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public string ExpandGlyph => IsExpanded ? "\uE70D" : "\uE76C";

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class FileLockDialog : UserControl
{
    private IReadOnlyList<string> _paths = [];
    private IReadOnlyList<FileLockProcess> _processes = [];
    private IReadOnlyList<FileLockNode> _nodes = [];
    private bool _busy;
    private bool _deviceEjectMode;
    private Func<Task>? _retryEjectAsync;

    public FileLockDialog()
    {
        InitializeComponent();
        UnlockLabel.Text = StringTable.Get("Lock_Unlock");
        DeleteLabel.Text = StringTable.Get("Lock_Delete");
        OtherLabel.Text = StringTable.Get("Lock_Other");
        EndTaskItem.Text = StringTable.Get("Lock_EndTask");
        ProcessesHeader.Text = StringTable.Get("Lock_Processes");
        EmptyText.Text = StringTable.Get("Lock_Empty");
        ToolTipService.SetToolTip(BrowseButton, StringTable.Get("Lock_Browse"));
        ToolTipService.SetToolTip(RefreshButton, StringTable.Get("Lock_Refresh"));
        RequestedTheme = ContentDialogTheme.Resolve(this);
        Unloaded += (_, _) => ShellIconBinder.Clear(TargetIcon, TargetGlyph);
    }

    public void ReleaseIcons()
    {
        ShellIconBinder.Clear(TargetIcon, TargetGlyph);
    }

    public event EventHandler? DeleteRequested;

    public void ShowError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        HideTree();
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
    }

    public IReadOnlyList<string> Paths => _paths;

    public IReadOnlyList<FileLockProcess> Processes => _processes;

    public Func<Task>? ReleasePreviewAsync { get; set; }

    public void ConfigureForDeviceEject(Func<Task> retryEjectAsync)
    {
        ArgumentNullException.ThrowIfNull(retryEjectAsync);
        _deviceEjectMode = true;
        _retryEjectAsync = retryEjectAsync;
        BrowseButton.Visibility = Visibility.Collapsed;
        DeleteButton.Visibility = Visibility.Collapsed;
        OtherButton.Visibility = Visibility.Collapsed;
        UnlockLabel.Text = StringTable.Get("Device_EjectReleaseOwn");
        RetryEjectButton.Content = StringTable.Get("Device_EjectRetry");
        RetryEjectButton.Visibility = Visibility.Visible;
        SyncButtons();
    }

    public IReadOnlyList<string> TargetsToDelete()
    {
        var files = SelectedNodes()
            .Select(node => node.LockedPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return files.Length > 0 ? files : _paths;
    }

    public async Task SetPathsAsync(IReadOnlyList<string> paths)
    {
        _paths = Normalize(paths);
        PathBox.Text = _paths.Count <= 1 ? _paths.FirstOrDefault() ?? string.Empty : string.Join("; ", _paths);
        PathText.Text = PathBox.Text;
        EndPathEdit();
        BindTargetIcon();
        await ReloadAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _paths = Normalize(PathBox.Visibility == Visibility.Visible ? PathBox.Text : PathText.Text);
        BindTargetIcon();
        _ = ReloadAsync();
    }

    private void PathText_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_deviceEjectMode) return;
        BeginPathEdit();
        e.Handled = true;
    }

    private void PathBox_GettingFocus(UIElement sender, GettingFocusEventArgs e)
    {
        if (e.FocusState == FocusState.Programmatic && PathBox.Visibility != Visibility.Visible)
        {
            e.TryCancel();
        }
    }

    private void PathBox_LostFocus(object sender, RoutedEventArgs e) => EndPathEdit(commit: true);

    private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            PathBox.Text = PathText.Text;
            EndPathEdit();
            return;
        }

        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        EndPathEdit(commit: true);
        Refresh_Click(sender, e);
    }

    private void BeginPathEdit()
    {
        PathText.Visibility = Visibility.Collapsed;
        PathBox.Text = PathText.Text;
        PathBox.Visibility = Visibility.Visible;
        PathBox.IsTabStop = true;
        PathBox.Focus(FocusState.Pointer);
        PathBox.SelectAll();
    }

    private void EndPathEdit(bool commit = false)
    {
        if (commit)
        {
            PathText.Text = PathBox.Text;
        }
        else
        {
            PathBox.Text = PathText.Text;
        }

        PathBox.IsTabStop = false;
        PathBox.Visibility = Visibility.Collapsed;
        PathText.Visibility = Visibility.Visible;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _deviceEjectMode)
        {
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        if (App.CurrentWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, window.NativeHandle);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await SetPathsAsync([file.Path]).ConfigureAwait(true);
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || ReleasePreviewAsync is null)
        {
            return;
        }

        _busy = true;
        try
        {
            await ReleasePreviewAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
            SyncButtons();
        }
    }

    private async void RetryEject_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _retryEjectAsync is null) return;
        _busy = true;
        SyncButtons();
        try { await _retryEjectAsync().ConfigureAwait(true); }
        catch (Exception error) { ShowError(error.Message); }
        finally { _busy = false; SyncButtons(); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceEjectMode || _busy || TargetsToDelete().Count == 0)
        {
            return;
        }

        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void EndTask_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceEjectMode) return;
        var processes = SelectedNodes()
            .Select(node => node.Process)
            .Where(process => process.CanTerminate)
            .DistinctBy(process => process.ProcessId)
            .ToArray();
        if (_busy || processes.Length == 0)
        {
            return;
        }

        var confirmation = new ContentDialog
        {
            Title = StringTable.Get("Lock_EndTask"), Content = StringTable.Get("Lock_EndWarning"),
            PrimaryButtonText = StringTable.Get("Lock_EndTask"), CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
        };
        ContentDialogTheme.Apply(confirmation, this);
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        _busy = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var process in processes)
                {
                    if (!FileLockQuery.Terminate(process)) throw new InvalidOperationException();
                }
            }).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            EmptyText.Text = StringTable.Get("Lock_EndFailed");
            EmptyText.Visibility = Visibility.Visible;
            HideTree();
        }
        finally
        {
            _busy = false;
            SyncButtons();
        }
    }

    private async Task ReloadAsync()
    {
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        HideTree();
        EmptyText.Visibility = Visibility.Collapsed;
        UnlockButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        OtherButton.IsEnabled = false;
        var paths = _paths;
        IReadOnlyList<FileLockProcess> processes;
        try
        {
            processes = await Task.Run(() => FileLockQuery.Find(paths)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            processes = [];
            EmptyText.Text = StringTable.Get("Lock_ScanFailed");
            EmptyText.Visibility = Visibility.Visible;
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            BindNodes([]);
            return;
        }

        _processes = processes;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        if (processes.Count == 0)
        {
            EmptyText.Text = StringTable.Get(_deviceEjectMode ? "Device_EjectNoFileLocks" : "Lock_Empty");
            EmptyText.Visibility = Visibility.Visible;
            BindNodes([]);
            SyncButtons();
            return;
        }

        BindNodes([.. processes.Select(process => new FileLockNode(process))]);
        ProcessScroll.Opacity = 1;
        ProcessScroll.IsHitTestVisible = true;
        SyncButtons();
    }

    private void BindNodes(IReadOnlyList<FileLockNode> nodes)
    {
        foreach (var node in Enumerate(_nodes))
        {
            node.PropertyChanged -= Node_PropertyChanged;
        }

        _nodes = nodes;
        foreach (var node in Enumerate(_nodes))
        {
            node.PropertyChanged += Node_PropertyChanged;
        }

        ProcessList.ItemsSource = nodes;
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileLockNode node } && node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
        }
    }

    private void Node_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(FileLockNode.IsSelected))
        {
            SyncButtons();
        }
    }

    private void SyncButtons()
    {
        var selected = SelectedNodes();
        UnlockButton.IsEnabled = !_busy && ReleasePreviewAsync is not null;
        DeleteButton.IsEnabled = !_deviceEjectMode && !_busy && TargetsToDelete().Count > 0;
        OtherButton.IsEnabled = !_deviceEjectMode && !_busy && selected.Any(node => node.Process.CanTerminate);
        EndTaskItem.IsEnabled = OtherButton.IsEnabled;
        RetryEjectButton.IsEnabled = !_busy && _retryEjectAsync is not null;
    }

    private IReadOnlyList<FileLockNode> SelectedNodes() =>
        [.. Enumerate(_nodes).Where(node => node.IsSelected)];

    private static IEnumerable<FileLockNode> Enumerate(IReadOnlyList<FileLockNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in node.Children)
            {
                yield return child;
            }
        }
    }

    private void HideTree()
    {
        ProcessScroll.Opacity = 0;
        ProcessScroll.IsHitTestVisible = false;
    }

    private void BindTargetIcon()
    {
        var path = _paths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path))
        {
            ShellIconBinder.Clear(TargetIcon, TargetGlyph);
            return;
        }

        var directory = Directory.Exists(path);
        TargetGlyph.Glyph = directory ? "\uE8B7" : "\uE8A5";
        ShellIconBinder.BindPath(TargetIcon, TargetGlyph, path, directory, 20);
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? paths) =>
        [.. (paths ?? []).SelectMany(Normalize)];

    private static IReadOnlyList<string> Normalize(string? text) =>
        [.. (text ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !string.IsNullOrWhiteSpace(path))];
}
