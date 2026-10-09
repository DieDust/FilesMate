using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FeatureTour;

// Retained by the page host across Previous/Next; never serialized to user preferences.
internal sealed class FeatureTourPracticeState
{
    public bool RootExpanded, ChildExpanded, FolderOpened, FileOpened;
    public bool PreviewOpened, PreviewVisible;
    public bool ImageFilter, DateFilter, SizeFilter, FilterUsed, FilterCleared;
    public bool ViewChanged, TabChanged, ThemeChanged, AccentChanged, Completed;
    public string Selected = "", Query = "", Tab = "Project";
    public FolderViewKind View = FolderViewKind.Details;
    public ElementTheme Theme = ElementTheme.Default;
    public bool GreenAccent;
    public void Reset()
    {
        RootExpanded = ChildExpanded = FolderOpened = FileOpened = false;
        PreviewOpened = PreviewVisible = false;
        ImageFilter = DateFilter = SizeFilter = FilterUsed = FilterCleared = false;
        ViewChanged = TabChanged = ThemeChanged = AccentChanged = Completed = false;
        Selected = Query = ""; Tab = "Project"; View = FolderViewKind.Details;
        Theme = ElementTheme.Default; GreenAccent = false;
    }
}

