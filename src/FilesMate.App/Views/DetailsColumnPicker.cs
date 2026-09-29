using System.Collections.ObjectModel;
using System.ComponentModel;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Theming;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FilesMate.App.Views;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class DetailsColumnChoice(DetailsColumn column, Action<DetailsColumnChoice> changed) : INotifyPropertyChanged
{
    public DetailsColumn Column { get; set; } = column;
    public string Title => Column.Title;
    public string PropertyName => Column.PropertyName ?? "";
    public bool CanHide => Column.Id != DetailsColumnId.Name;
    public bool IsChecked
    {
        get => Column.Visible;
        set
        {
            value = !CanHide || value;
            if (Column.Visible == value) return;
            Column = Column with { Visible = value };
            PropertyChanged?.Invoke(this, new(nameof(IsChecked)));
            changed(this);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class DetailsColumnPicker : Grid
{
    private readonly List<DetailsColumnChoice> _all = [];
    private readonly ObservableCollection<DetailsColumnChoice> _selected = [];
    private readonly ListView _available = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly ListView _order = new() { SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = "Title" };
    private readonly TextBox _search = new() { PlaceholderText = StringTable.Get("Columns_Search") };
    private readonly ComboBox _category = new() { MinWidth = 116 };
    private readonly NumberBox _width = new() { Minimum = 64, Maximum = 1200, SmallChange = 8, LargeChange = 40, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly Button _up = new() { Content = "↑", MinWidth = 36 };
    private readonly Button _down = new() { Content = "↓", MinWidth = 36 };
    private readonly HashSet<string> _initialKeys;
    private bool _syncing;
    private bool _reset;

    internal DetailsColumnPicker(DetailsColumn[] current, IReadOnlyList<ShellPropertyColumn> catalog)
    {
        RowSpacing = 10; ColumnSpacing = 16;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        ColumnDefinitions.Add(new() { Width = new GridLength(1.3, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var hint = new TextBlock { Text = StringTable.Get("Columns_ChooseHint"), TextWrapping = TextWrapping.Wrap };
        Add(hint, 0, 0, 2);
        var filters = new Grid { ColumnSpacing = 8 };
        filters.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.Children.Add(_search); Grid.SetColumn(_category, 1); filters.Children.Add(_category);
        Add(filters, 1, 0);
        var selectedLabel = new TextBlock { Text = StringTable.Get("Columns_Selected"), VerticalAlignment = VerticalAlignment.Center };
        Add(selectedLabel, 1, 1);
        static Border ListHost(ListView list)
        {
            var border = (Border)XamlReader.Load("""
                <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" CornerRadius="8" BorderThickness="1" Padding="4"
                    Background="{ThemeResource FilesMate.SettingsCard.BackgroundBrush}" BorderBrush="{ThemeResource FilesMate.Card.BorderBrush}" />
                """);
            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 32d));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 2, 8, 2)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(6)));
            list.ItemContainerStyle = style;
            list.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
            border.Child = list; return border;
        }
        Add(ListHost(_available), 2, 0); Add(ListHost(_order), 2, 1);
        _available.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <CheckBox Content="{Binding Title}" ToolTipService.ToolTip="{Binding PropertyName}" IsChecked="{Binding IsChecked, Mode=TwoWay}" IsEnabled="{Binding CanHide}"
                          MinHeight="28" HorizontalAlignment="Stretch" VerticalContentAlignment="Center" Padding="10,0,0,0"/>
            </DataTemplate>
            """);
        _order.ItemsSource = _selected;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        footer.Children.Add(_up); footer.Children.Add(_down);
        footer.Children.Add(new TextBlock { Text = StringTable.Get("Columns_Width"), VerticalAlignment = VerticalAlignment.Center });
        _width.Width = 108; footer.Children.Add(_width); Add(footer, 3, 1);
        var reset = new Button { Content = StringTable.Get("Columns_Reset") };
        foreach (var control in new Control[] { _up, _down, reset, _width, _search, _category }) control.CornerRadius = new CornerRadius(8);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_up, StringTable.Get("Columns_MoveUp"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_down, StringTable.Get("Columns_MoveDown"));
        reset.Click += (_, _) => Reset(); Add(reset, 3, 0);
        _initialKeys = current.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in current.Concat(catalog.Select(c => new DetailsColumn(DetailsColumnId.ShellProperty, c.Width, false, c.Name, c.Title))))
        {
            if (!seen.Add(column.Key)) continue;
            var choice = new DetailsColumnChoice(column, Changed);
            _all.Add(choice);
            if (column.Visible) _selected.Add(choice);
        }
        foreach (var category in new[] { "All", "Common", "Photo", "Media", "Document", "Other" })
            _category.Items.Add(new ComboBoxItem { Content = StringTable.Get("Columns_Category_" + category), Tag = category });
        _category.SelectedIndex = 0;
        _search.TextChanged += (_, _) => Filter();
        _category.SelectionChanged += (_, _) => Filter();
        _order.SelectionChanged += (_, _) => SyncSelection();
        _up.Click += (_, _) => Move(-1); _down.Click += (_, _) => Move(1);
        _width.ValueChanged += (_, _) =>
        {
            if (_syncing || _order.SelectedItem is not DetailsColumnChoice choice || !double.IsFinite(_width.Value)) return;
            choice.Column = choice.Column with { Width = Math.Clamp(_width.Value, choice.Column.Id == DetailsColumnId.Name ? 96 : 64, 1200) };
        };
        _order.SelectedIndex = 0;
        Filter(); SyncSelection();
    }

    internal DetailsColumn[] Result => DetailsColumn.Normalize(_selected.Select(c => c.Column).Concat(
        _all.Where(c => !c.IsChecked && (_reset ? c.Column.Id != DetailsColumnId.ShellProperty : _initialKeys.Contains(c.Column.Key))).Select(c => c.Column)));

    private void Add(FrameworkElement element, int row, int column, int span = 1)
    { SetRow(element, row); SetColumn(element, column); SetColumnSpan(element, span); Children.Add(element); }
    private void Changed(DetailsColumnChoice choice)
    {
        if (choice.IsChecked && !_selected.Contains(choice)) { _selected.Add(choice); _order.SelectedItem = choice; _order.ScrollIntoView(choice); }
        else if (!choice.IsChecked) _selected.Remove(choice);
        SyncSelection();
    }
    private void Filter()
    {
        var query = _search.Text.Trim();
        var category = (_category.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        _available.ItemsSource = _all.Where(c => (category == "All" || Category(c.Column) == category)
            && (c.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || c.PropertyName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.Column.Id == DetailsColumnId.ShellProperty).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    private static string Category(DetailsColumn column)
    {
        var name = column.PropertyName ?? "";
        if (column.Id != DetailsColumnId.ShellProperty) return "Common";
        if (name.StartsWith("System.Photo.") || name.StartsWith("System.Image.")) return "Photo";
        if (name.StartsWith("System.Audio.") || name.StartsWith("System.Video.") || name.StartsWith("System.Media.") || name.StartsWith("System.Music.")) return "Media";
        if (name.StartsWith("System.Document.") || name is "System.Author" or "System.Title" or "System.Subject" or "System.Keywords" or "System.Comment" or "System.Category") return "Document";
        return "Other";
    }
    private void SyncSelection()
    {
        _syncing = true;
        var index = _order.SelectedIndex;
        _up.IsEnabled = index > 0; _down.IsEnabled = index >= 0 && index < _selected.Count - 1;
        _width.IsEnabled = index >= 0;
        _width.Minimum = _order.SelectedItem is DetailsColumnChoice { CanHide: false } ? 96 : 64;
        _width.Value = (_order.SelectedItem as DetailsColumnChoice)?.Column.Width ?? 96;
        _syncing = false;
    }
    private void Move(int delta)
    {
        var index = _order.SelectedIndex; var target = index + delta;
        if (index < 0 || target < 0 || target >= _selected.Count) return;
        _selected.Move(index, target); _order.SelectedIndex = target; SyncSelection();
    }
    private void Reset()
    {
        _reset = true;
        foreach (var choice in _all) choice.IsChecked = false;
        _selected.Clear();
        foreach (var column in DetailsColumn.Defaults())
        {
            var choice = _all.Single(c => c.Column.Key == column.Key);
            choice.Column = column with { Visible = !column.Visible };
            choice.IsChecked = column.Visible;
        }
        _order.SelectedIndex = 0; Filter(); SyncSelection();
    }

    internal static async Task<DetailsColumn[]?> ShowAsync(FrameworkElement host, DetailsColumn[] current)
    {
        var catalog = await ShellProperties.GetColumnsAsync();
        if (!host.IsLoaded || host.XamlRoot is null) return null;
        var picker = new DetailsColumnPicker(current, catalog)
        {
            Width = Math.Max(440, Math.Min(720, host.XamlRoot.Size.Width - 96)),
            Height = Math.Max(240, Math.Min(480, host.XamlRoot.Size.Height - 180))
        };
        var dialog = new ContentDialog { Title = StringTable.Get("Columns_Title"), Content = picker,
            PrimaryButtonText = StringTable.Get("SearchPage_Apply"), CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary, XamlRoot = host.XamlRoot };
        dialog.Resources["ContentDialogMaxWidth"] = picker.Width + 48;
        ContentDialogTheme.Apply(dialog, host);
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? picker.Result : null;
    }
}
