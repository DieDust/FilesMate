using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Search;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private List<SearchCategory> _categories = [];
    private string _categoryId = "All";
    private bool _buildingCategories;
    private readonly System.Windows.Threading.DispatcherTimer _categoryEditTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Action? _categoryCommit;

    private static string CategoryLabel(SearchCategory category) => category.Builtin is { } builtin
        ? Loc.Get(builtin switch
        {
            SearchFilter.All => "Category_All", SearchFilter.Apps => "Application",
            SearchFilter.Executables => "SearchRankExecutable",
            SearchFilter.Documents => "SearchRankDocument", SearchFilter.Images => "SearchRankImage",
            SearchFilter.Media => "Category_Media", _ => "SearchRankFolder",
        }) : category.Name;

    private static string[] ParseCategoryExtensions(string text)
    {
        try { return SearchCategories.ParseExtensions(text); }
        catch (ArgumentException) { throw new ArgumentException(Loc.Get("Category_InvalidExtensions")); }
    }

    private void LoadCategories()
    {
        _categoryEditTimer.Tick += (_, _) => { _categoryEditTimer.Stop(); var commit = _categoryCommit; _categoryCommit = null; commit?.Invoke(); };
        _categories = SearchCategories.Load(_host.Profile);
        RenderCategoryButtons();
    }
    private void RenderCategoryButtons()
    {
        _buildingCategories = true;
        CategoryButtons.Children.Clear();
        var executablesEnabled = SearchExecutableConfiguration.Load(_host.Profile);
        var selected = _categories.FirstOrDefault(c => c.Visible && c.Id == _categoryId && (executablesEnabled || c.Builtin != SearchFilter.Executables))
            ?? _categories.First(c => c.Visible && (executablesEnabled || c.Builtin != SearchFilter.Executables));
        _categoryId = selected.Id;
        _filter = selected.Builtin ?? SearchFilter.All;
        foreach (var category in _categories.Where(c => c.Visible && (executablesEnabled || c.Builtin != SearchFilter.Executables)))
        {
            var button = new RadioButton { Content = CategoryLabel(category), Tag = category, GroupName = "Filter", Style = (Style)FindResource("Segment"),
                FontSize = 12, Padding = new Thickness(10, 5, 10, 5), IsChecked = category.Id == _categoryId,
                ToolTip = category.Builtin is null ? string.Join(", ", category.Extensions) : CategoryLabel(category) };
            button.Checked += Category_Changed;
            CategoryButtons.Children.Add(button);
        }
        _buildingCategories = false;
    }
    private void Category_Changed(object sender, RoutedEventArgs e)
    {
        if (_buildingCategories || !_ready || sender is not RadioButton { Tag: SearchCategory category }) return;
        _categoryId = category.Id;
        _filter = category.Builtin ?? SearchFilter.All;
        _offset = 0; _hasMore = false;
        Search();
        QueryBox.Focus();
    }
    private void Categories_Open(object sender, RoutedEventArgs e)
    {
        CancelSearch();
        SettingsPanel.Visibility = Visibility.Collapsed;
        CategoriesPanel.Visibility = Visibility.Visible;
        CategoryStatus.Text = "";
        RenderCategoryEditors();
    }
    private void Categories_Back(object sender, RoutedEventArgs e) { CategoriesPanel.Visibility = Visibility.Collapsed; ShowSettings(true, ""); }
    private void Categories_Reset(object sender, RoutedEventArgs e) => PersistCategories(SearchCategories.Defaults());
    private void Category_Add(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_categories.Count >= 32) throw new ArgumentException(Loc.Get("Category_Limit"));
            var name = CategoryName.Text.Trim();
            if (name.Length == 0) throw new ArgumentException(Loc.Get("Category_NameRequired"));
            var category = new SearchCategory(Guid.NewGuid().ToString("N"), name, null, ParseCategoryExtensions(CategoryExtensions.Text));
            if (PersistCategories([.. _categories, category])) { CategoryName.Clear(); CategoryExtensions.Clear(); }
        }
        catch (ArgumentException error) { CategoryStatus.Text = error.Message; }
    }
    private bool PersistCategories(List<SearchCategory> proposed, bool render = true)
    {
        try
        {
            if (proposed.All(c => !c.Visible)) throw new ArgumentException(Loc.Get("Category_VisibleRequired"));
            if (proposed.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new ArgumentException(Loc.Get("Category_Duplicate"));
            SearchCategories.Save(_host.Profile, proposed);
            _categories = SearchCategories.Normalize(proposed);
            RenderCategoryButtons();
            if (render) RenderCategoryEditors();
            CategoryStatus.Text = "";
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { CategoryStatus.Text = error.Message; return false; }
    }
    private void RenderCategoryEditors()
    {
        _categoryEditTimer.Stop(); _categoryCommit = null;
        CategoryEditors.Children.Clear();
        foreach (var category in _categories)
        {
            var id = category.Id;
            var line = new Grid();
            foreach (var width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(48), new GridLength(32), new GridLength(32), new GridLength(32) })
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            var name = new TextBox { Text = category.Name, MaxLength = 24, IsReadOnly = category.Builtin is not null, Margin = new Thickness(0, 0, 8, 0) };
            var fields = new StackPanel();
            if (category.Builtin is null) fields.Children.Add(name);
            else fields.Children.Add(new TextBlock { Text = CategoryLabel(category), Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(fields);
            TextBox? suffix = null;
            if (category.Builtin is null)
            {
                suffix = new TextBox { Text = string.Join(", ", category.Extensions), MaxLength = 1024, Margin = new Thickness(0, 6, 8, 0), ToolTip = Loc.Get("Category_ExtensionsExample") };
                fields.Children.Add(suffix);
                void Commit(object s, RoutedEventArgs e)
                {
                    try
                    {
                        var title = name.Text.Trim();
                        if (title.Length == 0) throw new ArgumentException(Loc.Get("Category_NameRequired"));
                        var extensions = ParseCategoryExtensions(suffix.Text);
                        PersistCategories(_categories.Select(c => c.Id == id ? c with { Name = title, Extensions = extensions } : c).ToList(), render: false);
                    }
                    catch (ArgumentException error) { CategoryStatus.Text = error.Message; }
                }
                name.LostKeyboardFocus += Commit; suffix.LostKeyboardFocus += Commit;
                void Schedule(object s, TextChangedEventArgs e)
                {
                    _categoryEditTimer.Stop();
                    _categoryCommit = () => Commit(s, e);
                    _categoryEditTimer.Start();
                }
                name.TextChanged += Schedule; suffix.TextChanged += Schedule;
            }
            var toggle = new ToggleButton { Style = (Style)FindResource("Switch"), IsChecked = category.Visible, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.Get("Category_Show") };
            Grid.SetColumn(toggle, 1); line.Children.Add(toggle);
            toggle.Click += (_, _) => { if (!PersistCategories(_categories.Select(c => c.Id == id ? c with { Visible = toggle.IsChecked == true } : c).ToList())) toggle.IsChecked = category.Visible; };
            void AddButton(int column, string label, int move)
            {
                var button = new Button { Content = label, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(button, column); line.Children.Add(button);
                button.Click += (_, _) =>
                {
                    var list = _categories.ToList(); var index = list.FindIndex(c => c.Id == id);
                    if (move == 0) list.RemoveAt(index);
                    else { var target = index + move; if (target < 0 || target >= list.Count) return; (list[index], list[target]) = (list[target], list[index]); }
                    PersistCategories(list);
                };
            }
            AddButton(2, "↑", -1); AddButton(3, "↓", 1);
            if (category.Builtin is null) AddButton(4, "×", 0);
            CategoryEditors.Children.Add(new Border { Style = (Style)FindResource("SettingsCard"), Child = line });
        }
    }
}
