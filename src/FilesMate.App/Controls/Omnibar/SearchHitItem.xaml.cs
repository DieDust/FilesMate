using FilesMate.App.Icons;
using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class SearchHitItem : UserControl
{
    public SearchHitItem()
    {
        InitializeComponent();
        DataContextChanged += SearchHitItem_DataContextChanged;
        Unloaded += (_, _) => ShellIconBinder.Clear(IconImage, Glyph);
    }

    private void SearchHitItem_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is not HomeSearchHit hit)
        {
            ShellIconBinder.Clear(IconImage, Glyph);
            NameText.Text = string.Empty;
            PathText.Text = string.Empty;
            ToolTipService.SetToolTip(this, null);
            return;
        }

        NameText.Text = hit.Name;
        PathText.Text = hit.Path;
        ToolTipService.SetToolTip(this, hit.Path);
        Glyph.Glyph = hit.IsDirectory ? "\uE8B7" : "\uE8A5";
        ShellIconBinder.BindPath(IconImage, Glyph, hit.Path, hit.IsDirectory, 20);
    }
}
