using FilesMate.App.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Views;

public sealed partial class FileLockNodeView : UserControl
{
    private FileLockNode? _node;
    private bool _hover;

    public FileLockNodeView()
    {
        InitializeComponent();
        DataContextChanged += FileLockNodeView_DataContextChanged;
        AddHandler(PointerPressedEvent, new PointerEventHandler(FileLockNodeView_PointerPressed), handledEventsToo: true);
        PointerEntered += (_, _) => ApplyChrome(hover: true);
        PointerExited += (_, _) => ApplyChrome(hover: false);
        Unloaded += (_, _) =>
        {
            Detach();
            ShellIconBinder.Clear(IconImage, Glyph);
        };
    }

    private void FileLockNodeView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var node = ResolveNode();
        if (node is null)
        {
            return;
        }

        node.IsSelected = !node.IsSelected;
        e.Handled = true;
    }

    private FileLockNode? ResolveNode()
    {
        if (_node is not null)
        {
            return _node;
        }

        for (var current = (DependencyObject)this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: FileLockNode node })
            {
                return node;
            }
        }

        return null;
    }

    private void FileLockNodeView_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        Detach();
        _node = args.NewValue as FileLockNode
            ?? (args.NewValue as TreeViewNode)?.Content as FileLockNode;
        if (_node is null)
        {
            ShellIconBinder.Clear(IconImage, Glyph);
            TitleText.Text = string.Empty;
            SubtitleText.Text = string.Empty;
            SubtitleText.Visibility = Visibility.Collapsed;
            Root.Background = null;
            Root.BorderBrush = null;
            return;
        }

        _node.PropertyChanged += Node_PropertyChanged;
        TitleText.Text = _node.Title;
        SubtitleText.Text = _node.Subtitle;
        SubtitleText.Visibility = string.IsNullOrWhiteSpace(_node.Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
        Glyph.Glyph = _node.IsProcess ? "\uE7EF" : _node.IconIsDirectory ? "\uE8B7" : "\uE8A5";
        ApplyChrome(hover: false);
        if (string.IsNullOrWhiteSpace(_node.IconPath))
        {
            ShellIconBinder.Clear(IconImage, Glyph);
            return;
        }

        ShellIconBinder.BindPath(IconImage, Glyph, _node.IconPath, _node.IconIsDirectory, 20);
    }

    private void Node_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(FileLockNode.IsSelected))
        {
            ApplyChrome(_hover);
        }
    }

    private void ApplyChrome(bool hover)
    {
        _hover = hover;
        var selected = _node is { IsSelected: true };
        var fill = selected
            ? hover ? "FilesMate.Item.SelectedHoverBrush" : "FilesMate.Item.SelectedBrush"
            : hover ? "FilesMate.Item.HoverBrush" : null;
        Root.Background = fill is null ? null : Theme(fill);
        Root.BorderBrush = selected ? Theme("FilesMate.Glass.BorderStrongBrush") : null;
    }

    private static Brush? Theme(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : null;

    private void Detach()
    {
        if (_node is not null)
        {
            _node.PropertyChanged -= Node_PropertyChanged;
            _node = null;
        }
    }
}
