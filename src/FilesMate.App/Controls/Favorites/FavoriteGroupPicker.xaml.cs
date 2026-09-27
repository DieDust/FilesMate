using FilesMate.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Controls.Favorites;

/// <summary>One inline hierarchy for quick-save and multi-file bookmarks; never probes bookmarked paths.</summary>
public sealed partial class FavoriteGroupPicker : UserControl
{
    public sealed record GroupChoice(string? Id, string Name, string Breadcrumb);
    private readonly Dictionary<string, TreeViewNode> _nodes = new(StringComparer.Ordinal);
    private Dictionary<string, FavoriteEntry> _groups = [];
    private ILookup<string, FavoriteEntry> _children = Array.Empty<FavoriteEntry>().ToLookup(entry => "");
    private bool _refreshing;
    private bool _saving;
    private string? _newParent;
    public string? SelectedGroupId { get; private set; }
    public bool IsSaving => _saving;

    public FavoriteGroupPicker(string? selectedGroupId = null)
    {
        InitializeComponent();
        ProtectedCursor = Input.DesktopCursors.Arrow;
        Reload(selectedGroupId);
    }

    private void Reload(string? selectedGroupId)
    {
        _refreshing = true;
        try
        {
            _groups = App.Favorites.Entries.Where(entry => entry.IsGroup).ToDictionary(entry => entry.Id);
            _children = _groups.Values.ToLookup(entry => entry.GroupId ?? "");
            _nodes.Clear(); GroupTree.RootNodes.Clear();
            var root = Node(null, Loc.Get("Favorites_Bar"), Loc.Get("Favorites_Bar"));
            GroupTree.RootNodes.Add(root);
            Populate(root); root.IsExpanded = true;
            SelectGroup(selectedGroupId);
        }
        finally { _refreshing = false; }
    }

    private TreeViewNode Node(string? id, string name, string breadcrumb)
    {
        var node = new TreeViewNode { Content = new GroupChoice(id, name, breadcrumb), HasUnrealizedChildren = _children[id ?? ""].Any() };
        _nodes[id ?? ""] = node;
        return node;
    }

    private void Populate(TreeViewNode node)
    {
        if (!node.HasUnrealizedChildren || node.Content is not GroupChoice group) return;
        foreach (var entry in _children[group.Id ?? ""])
        {
            var breadcrumb = group.Breadcrumb + " › " + entry.Name;
            if (breadcrumb.Length > 240) breadcrumb = "…" + breadcrumb[^220..];
            node.Children.Add(Node(entry.Id, entry.Name, breadcrumb));
        }
        node.HasUnrealizedChildren = false;
    }

    public void SelectGroup(string? id)
    {
        if (id is not null && !_groups.ContainsKey(id)) throw new InvalidOperationException(Loc.Get("Favorites_MissingGroup"));
        var chain = new Stack<string>();
        for (var parent = id; parent is not null; parent = _groups[parent].GroupId) chain.Push(parent);
        var node = _nodes[""];
        while (chain.TryPop(out var child))
        {
            Populate(node); node.IsExpanded = true;
            node = _nodes[child];
        }
        GroupTree.SelectedNode = node;
        UpdateSelection(node);
    }

    private void UpdateSelection(TreeViewNode node)
    {
        if (node.Content is not GroupChoice choice) return;
        SelectedGroupId = choice.Id;
        SelectedName.Text = choice.Name;
        SelectedParent.Text = (node.Parent?.Content as GroupChoice)?.Breadcrumb ?? "";
        SelectedParent.Visibility = SelectedParent.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(LocationButton, choice.Breadcrumb);
        AutomationProperties.SetName(LocationButton, Loc.Get("Favorites_AddTo") + ": " + choice.Breadcrumb);
    }

    private void GroupTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args) => Populate(args.Node);
    private void GroupTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (!_refreshing && sender.SelectedNode is { } node) UpdateSelection(node);
    }

    private void Location_Click(object sender, RoutedEventArgs e)
    {
        var opening = Browser.Visibility != Visibility.Visible;
        Browser.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        LocationChevron.Glyph = opening ? "\uE70E" : "\uE70D";
        if (!opening) EndNameEdit();
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        _newParent = SelectedGroupId;
        NewGroupParent.Text = Loc.Get("Favorites_AddTo") + ": " + ((GroupChoice)_nodes[_newParent ?? ""].Content).Breadcrumb;
        NewGroupName.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
        NameEditor.Visibility = Visibility.Visible;
        NewGroupButton.Visibility = Visibility.Collapsed;
        NewGroupName.Focus(FocusState.Programmatic);
    }

    private void EndNameEdit()
    {
        NameEditor.Visibility = Visibility.Collapsed;
        NewGroupButton.Visibility = Visibility.Visible;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => EndNameEdit();
    private async void Create_Click(object sender, RoutedEventArgs e) => await CreateGroupAsync();
    private async void NewGroupName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await CreateGroupAsync(); }
        else if (e.Key == VirtualKey.Escape) { e.Handled = true; EndNameEdit(); NewGroupButton.Focus(FocusState.Programmatic); }
    }
    private async Task CreateGroupAsync()
    {
        if (_saving) return;
        _saving = true;
        LocationButton.IsEnabled = GroupTree.IsEnabled = NewGroupName.IsEnabled = CreateButton.IsEnabled = CancelButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var created = await App.Favorites.CreateGroupAsync(NewGroupName.Text, _newParent);
            Reload(created.Id);
            EndNameEdit();
            LocationButton.Focus(FocusState.Programmatic);
        }
        catch (Exception error) { ErrorText.Text = error.Message; ErrorText.Visibility = Visibility.Visible; }
        finally
        {
            _saving = false;
            LocationButton.IsEnabled = GroupTree.IsEnabled = NewGroupName.IsEnabled = CreateButton.IsEnabled = CancelButton.IsEnabled = true;
        }
    }
}
