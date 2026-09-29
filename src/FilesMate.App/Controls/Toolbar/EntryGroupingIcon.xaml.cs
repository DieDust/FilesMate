using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Toolbar;

public sealed partial class EntryGroupingIcon : UserControl
{
    public EntryGroupingIcon() => InitializeComponent();

    public void SetGrouping(EntryGrouping grouping)
    {
        FoldersFirst.Visibility = grouping == EntryGrouping.FoldersFirst ? Visibility.Visible : Visibility.Collapsed;
        FilesFirst.Visibility = grouping == EntryGrouping.FilesFirst ? Visibility.Visible : Visibility.Collapsed;
        Mixed.Visibility = grouping == EntryGrouping.Mixed ? Visibility.Visible : Visibility.Collapsed;
    }
}
