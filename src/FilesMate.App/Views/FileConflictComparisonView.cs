using FilesMate.App.Controls.Preview;
using FilesMate.App.Localization;
using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Processes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;

namespace FilesMate.App.Views;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class FileComparisonLine(TextDifferenceRow row)
{
    public string Change => row.Kind != TextDifferenceKind.Changed ? "Equal"
        : row.IncomingLine is null ? "Deleted" : row.ExistingLine is null ? "Added" : "Modified";
    public string Incoming => row.Incoming;
    public string Existing => row.Existing;
    // A deletion is shown as an explicitly struck-out annotation in the new side,
    // never as a line number or live content belonging to the incoming file.
    public string IncomingDisplay => Change == "Deleted" ? row.Existing : row.Incoming;
    public string IncomingNumber => Change == "Deleted" ? "−" : row.IncomingLine is { } number
        ? $"{(Change == "Equal" ? " " : Change == "Modified" ? "~" : "+")} {number}" : "";
    public string ExistingNumber => row.ExistingLine?.ToString() ?? "";
    public string DeletedCaption => StringTable.Get("Conflict_DeletedAnnotation");
}

internal sealed class FileConflictComparisonView : Grid, IAsyncDisposable
{
    private readonly FileConflict _conflict;
    private readonly string _incoming;
    private readonly CancellationTokenSource _lifetime;
    private readonly TextBlock _status = FileConflictBody.Label(StringTable.Get("Conflict_Loading"), 12);
    private readonly Grid _content = new();
    private readonly Grid _toolbar = new() { ColumnSpacing = 8, RowSpacing = 6 };
    private readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ListView _lines = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
        Padding = new Thickness(0), ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection() };
    private readonly Grid _previews = new() { ColumnSpacing = 12, Visibility = Visibility.Collapsed };
    private readonly PreviewPane _left = new();
    private readonly PreviewPane _right = new();
    private readonly PreviewService _leftService = Service();
    private readonly PreviewService _rightService = Service();
    private readonly Button _diff;
    private readonly Button _preview;
    private readonly Button _verify;
    private Task _load = Task.CompletedTask;
    private Task _previewLoad = Task.CompletedTask;
    private Task _verification = Task.CompletedTask;
    private bool _started, _disposed, _previewsStarted, _previewSelectedByUser;
    private FileComparisonLine[] _rows = [];
    private bool _showingDiff;
    public double PreferredHeight { get; private set; } = 330;
    public event EventHandler? PreferredHeightChanged;

    public FileConflictComparisonView(FileConflict conflict, CancellationToken token)
    {
        _conflict = conflict; _incoming = conflict.SourcePreviewPath ?? conflict.Source;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        RowSpacing = 6;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        _diff = Tab("Conflict_TextChanges", "ConflictTextChanges"); _diff.IsEnabled = false;
        _preview = Tab("Conflict_Preview", "ConflictPreview");
        _verify = Tab("Conflict_Verify", "ConflictVerify");
        _verify.IsEnabled = conflict.Incoming is not null && conflict.Existing is not null && !conflict.DestinationIsLink;
        _diff.Click += (_, _) => { _previewSelectedByUser = false; ShowDiff(); };
        _preview.Click += (_, _) => { _previewSelectedByUser = true; ShowPreviews(); };
        _verify.Click += (_, _) => _verification = VerifyAsync();
        modes.Children.Add(_diff); modes.Children.Add(_preview);
        var modeBand = new Border { Child = modes, Padding = new Thickness(3), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
        ThemeResources.Bind(modeBand, Border.BackgroundProperty, "FilesMate.Item.HoverBrush");
        _toolbar.Children.Add(modeBand);
        _tools.Children.Add(_verify); _tools.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_tools, 1); _toolbar.Children.Add(_tools);
        Children.Add(_toolbar);
        Grid.SetRow(_status, 1); Children.Add(_status);
        var contentFrame = new Border { Child = _content, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4) };
        ThemeResources.Bind(contentFrame, Border.BorderBrushProperty, "FilesMate.Card.BorderBrush");
        ThemeResources.Bind(contentFrame, Border.BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
        Grid.SetRow(contentFrame, 2); Children.Add(contentFrame);
        ThemeResources.Bind(_content, BackgroundProperty, "FilesMate.FileContent.BackgroundBrush");
        _content.Children.Add(_lines); _content.Children.Add(_previews);
        ScrollViewer.SetVerticalScrollMode(_lines, ScrollMode.Enabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_lines, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(_lines, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(_lines, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_lines, StringTable.Get("Conflict_TextChanges"));
        _lines.ItemTemplateSelector = new DifferenceTemplates();
        _lines.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
                <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                <Setter Property="Padding" Value="0"/><Setter Property="MinHeight" Value="24"/>
                <Setter Property="IsTabStop" Value="False"/>
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListViewItem">
                    <ContentPresenter Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" HorizontalContentAlignment="Stretch"/>
                </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
        _previews.ColumnDefinitions.Add(new()); _previews.ColumnDefinitions.Add(new());
        _previews.Children.Add(_left); Grid.SetColumn(_right, 1); _previews.Children.Add(_right);
        _left.UseForComparison(); _right.UseForComparison();
        _left.Attach(_leftService); _right.Attach(_rightService);
        // ContentDialog can reparent content while opening. A PreviewPane clears
        // on unload, so start/restart the pair only after both panes are attached.
        _left.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, TryStartPreviews);
        _right.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, TryStartPreviews);
        _left.Unloaded += (_, _) => _previewsStarted = false;
        _right.Unloaded += (_, _) => _previewsStarted = false;
        var executable = ExternalFileComparison.FindWinMerge();
        if (executable is not null && !conflict.DestinationIsLink && (conflict.CanReplace || conflict.CanMerge))
        {
            var external = Tab("Conflict_WinMerge", "ConflictWinMerge");
            external.Click += (_, _) =>
            {
                try { ExternalFileComparison.Open(executable, conflict.Destination, _incoming); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { _status.Text = StringTable.Get("Conflict_CompareFailed"); }
            };
            _tools.Children.Add(external);
        }
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        SizeChanged += (_, _) =>
        {
            var narrow = ActualWidth < 560;
            Grid.SetColumn(_tools, narrow ? 0 : 1); Grid.SetRow(_tools, narrow ? 1 : 0);
            Grid.SetColumnSpan(_tools, narrow ? 2 : 1);
            _tools.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            UpdatePreferredHeight();
        };
        _toolbar.SizeChanged += (_, _) => UpdatePreferredHeight();
        _status.SizeChanged += (_, _) => UpdatePreferredHeight();
    }

    private static PreviewService Service() => new([new ImagePreviewProvider(), new TextPreviewProvider(), new PdfPreviewProvider(),
        new OfficePreviewProvider(), new MediaPreviewProvider(), new PropertiesPreviewProvider()]);

    private static Button Tab(string key, string id)
    {
        // Toolbar styles have a fixed 28-DIP height and clip taller fonts once padding is added.
        var label = FileConflictBody.Label(StringTable.Get(key), 13);
        label.TextWrapping = TextWrapping.NoWrap;
        var button = new Button { Content = label, Padding = new Thickness(10, 5, 10, 5),
            FontSize = 13, MinHeight = 30, CornerRadius = new CornerRadius(6), VerticalContentAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["QuietButtonStyle"] };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label.Text); return button;
    }

    public void Start()
    {
        void Begin()
        {
            if (_started || _disposed) return;
            _started = true; _load = LoadAsync();
        }
        if (IsLoaded) Begin(); else Loaded += (_, _) => Begin();
    }

    private async Task LoadAsync()
    {
        try
        {
            var comparison = _conflict.Incoming is null || _conflict.Existing is null || _conflict.DestinationIsLink ? null
                : await Task.Run(() => FileContentComparison.ReadTextAsync(_incoming, _conflict.Destination, _lifetime.Token), _lifetime.Token);
            if (_disposed) return;
            if (comparison is null) { _status.Text = StringTable.Get("Conflict_PreviewHint"); ShowPreviews(); return; }
            _rows = comparison.Difference.Rows.Select(row => new FileComparisonLine(row)).ToArray();
            _lines.ItemsSource = _rows;
            _diff.IsEnabled = true;
            var difference = comparison.Difference;
            _status.Text = comparison.IsPartial ? StringTable.Get("Conflict_Partial")
                : comparison.SameBytes == true ? StringTable.Get("Conflict_Identical")
                : difference.AddedLines + difference.RemovedLines == 0 ? StringTable.Get("Conflict_EncodingDiffers")
                : StringTable.Format("Conflict_ChangeCounts", _rows.Count(r => r.Change == "Added"),
                    _rows.Count(r => r.Change == "Deleted"), _rows.Count(r => r.Change == "Modified"));
            if (difference.IsSimplified) _status.Text += " · " + StringTable.Get("Conflict_Coarse");
            if (!_previewSelectedByUser) ShowDiff();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (!_disposed) { _status.Text = StringTable.Get("Conflict_CompareFailed"); ShowPreviews(); } }
    }

    private void ShowDiff()
    {
        _showingDiff = true;
        _lines.Visibility = Visibility.Visible; _previews.Visibility = Visibility.Collapsed;
        ThemeResources.Bind(_diff, Button.BackgroundProperty, "FilesMate.Compare.SourceFillBrush");
        ThemeResources.Bind(_preview, Button.BackgroundProperty, "FilesMate.TransparentBrush");
        UpdatePreferredHeight();
    }

    private void ShowPreviews()
    {
        _showingDiff = false;
        _lines.Visibility = Visibility.Collapsed; _previews.Visibility = Visibility.Visible;
        ThemeResources.Bind(_diff, Button.BackgroundProperty, "FilesMate.TransparentBrush");
        ThemeResources.Bind(_preview, Button.BackgroundProperty, "FilesMate.Compare.SourceFillBrush");
        UpdatePreferredHeight();
        TryStartPreviews();
    }

    private void TryStartPreviews()
    {
        if (_previewsStarted || _disposed || _previews.Visibility != Visibility.Visible || !_left.IsLoaded || !_right.IsLoaded) return;
        _previewsStarted = true; _left.SetVisible(true); _right.SetVisible(true);
        _previewLoad = Task.WhenAll(_left.LoadAsync(_conflict.Destination, 1, _lifetime.Token), _right.LoadAsync(_incoming, 1, _lifetime.Token));
    }

    private void UpdatePreferredHeight()
    {
        var contentHeight = 260d;
        if (_showingDiff)
        {
            var measure = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap };
            var width = Math.Max(80, (ActualWidth - 20) / 2 - 66);
            contentHeight = 0;
            foreach (var row in _rows)
            {
                measure.Text = row.Existing; measure.Measure(new(width, double.PositiveInfinity)); var left = measure.DesiredSize.Height;
                measure.Text = row.IncomingDisplay; measure.Measure(new(width, double.PositiveInfinity));
                contentHeight += Math.Max(26, Math.Max(left, measure.DesiredSize.Height + (row.Change == "Deleted" ? 18 : 0)) + 8);
                if (contentHeight >= 280) break;
            }
            contentHeight = Math.Clamp(contentHeight, 26, 280);
        }
        var height = Math.Max(36, _toolbar.ActualHeight) + Math.Max(18, _status.ActualHeight) + RowSpacing * 2 + contentHeight + 10;
        if (Math.Abs(PreferredHeight - height) < .5) return;
        PreferredHeight = height; PreferredHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task VerifyAsync()
    {
        _verify.IsEnabled = false; _status.Text = StringTable.Get("Conflict_Verifying");
        try
        {
            var same = await Task.Run(() => FileContentComparison.EqualBytesAsync(_incoming, _conflict.Destination, _lifetime.Token), _lifetime.Token);
            if (!_disposed) _status.Text = StringTable.Get(same ? "Conflict_Identical" : "Conflict_Different");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (!_disposed) _status.Text = StringTable.Get("Conflict_CompareFailed"); }
        finally { if (!_disposed) _verify.IsEnabled = true; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        _left.SetVisible(false); _right.SetVisible(false);
        try { await Task.WhenAll(_load, _previewLoad, _verification); }
        catch (OperationCanceledException) { }
        finally
        {
            await _leftService.DisposeAsync(); await _rightService.DisposeAsync(); _lifetime.Dispose();
        }
    }

    private sealed class DifferenceTemplates : DataTemplateSelector
    {
        private readonly DataTemplate _equal = Template("Equal");
        private readonly DataTemplate _added = Template("Added");
        private readonly DataTemplate _deleted = Template("Deleted");
        private readonly DataTemplate _modified = Template("Modified");
        protected override DataTemplate SelectTemplateCore(object item) => (item as FileComparisonLine)?.Change switch
        { "Added" => _added, "Deleted" => _deleted, "Modified" => _modified, _ => _equal };
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
        private static DataTemplate Template(string change)
        {
            var right = change != "Equal" ? $"FilesMate.Compare.{change}FillBrush" : "FilesMate.TransparentBrush";
            var rightMarker = change != "Equal" ? $"FilesMate.Compare.{change}TextBrush" : "FilesMate.Text.SecondaryBrush";
            var rightEdge = change != "Equal" ? rightMarker : "FilesMate.TransparentBrush";
            var ink = change == "Deleted" ? rightMarker : "FilesMate.Text.PrimaryBrush";
            var deletedVisibility = change == "Deleted" ? "Visible" : "Collapsed";
            var decoration = change == "Deleted" ? "Strikethrough" : "None";
            return (DataTemplate)XamlReader.Load($$"""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Grid ColumnSpacing="8" MinHeight="26">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                        <Border Background="{ThemeResource FilesMate.TransparentBrush}" Padding="6,4">
                            <Grid ColumnSpacing="8">
                                <Grid.ColumnDefinitions><ColumnDefinition Width="36"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                <TextBlock Text="{Binding ExistingNumber}" Tag="FilesMate.ContentTypography" FontFamily="Consolas" FontSize="11" Foreground="{ThemeResource FilesMate.Text.SecondaryBrush}"/>
                                <TextBlock Grid.Column="1" Text="{Binding Existing}" Tag="FilesMate.ContentTypography"
                                    FontFamily="Consolas" FontSize="13" TextWrapping="Wrap" IsTextSelectionEnabled="True"
                                    Foreground="{ThemeResource FilesMate.Text.PrimaryBrush}"/>
                            </Grid>
                        </Border>
                        <Border Grid.Column="1" Background="{ThemeResource {{right}}}" Padding="4,4,6,4" CornerRadius="3"
                                BorderBrush="{ThemeResource {{rightEdge}}}" BorderThickness="2,0,0,0">
                            <Grid ColumnSpacing="8">
                                <Grid.ColumnDefinitions><ColumnDefinition Width="36"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                <TextBlock Text="{Binding IncomingNumber}" Tag="FilesMate.ContentTypography" FontFamily="Consolas" FontSize="11" Foreground="{ThemeResource {{rightMarker}}}"/>
                                <StackPanel Grid.Column="1" Spacing="2">
                                    <TextBlock Text="{Binding DeletedCaption}" FontSize="11" Visibility="{{deletedVisibility}}"
                                               Foreground="{ThemeResource {{rightMarker}}}"/>
                                    <TextBlock Text="{Binding IncomingDisplay}" Tag="FilesMate.ContentTypography"
                                        FontFamily="Consolas" FontSize="13" TextWrapping="Wrap" IsTextSelectionEnabled="True"
                                        TextDecorations="{{decoration}}" Foreground="{ThemeResource {{ink}}}"/>
                                </StackPanel>
                            </Grid>
                        </Border>
                    </Grid>
                </DataTemplate>
                """);
        }
    }
}
