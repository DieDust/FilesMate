using FilesMate.App.Localization;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly Dictionary<string, UIElement> _sections = new(StringComparer.Ordinal);
    private readonly bool _initialized;
    private string? _pendingCategory;

    public event EventHandler? CloseRequested;

    public SettingsPage()
    {
        InitializeComponent();
        SettingsSubtitle.Text = StringTable.Get("Settings");
        SettingsCaption.Text = StringTable.Get("AppName");
        GeneralNavLabel.Text = StringTable.Get("SettingsGeneral");
        AppearanceNavLabel.Text = StringTable.Get("SettingsAppearance");
        FilesAndFoldersNavLabel.Text = StringTable.Get("SettingsFilesAndFolders");
        KeyboardNavLabel.Text = StringTable.Get("SettingsKeyboard");
        SearchNavLabel.Text = StringTable.Get("SettingsSearch");
        TagsNavLabel.Text = StringTable.Get("SettingsTags");
        AdvancedNavLabel.Text = StringTable.Get("SettingsAdvanced");
        AboutNavLabel.Text = StringTable.Get("SettingsAbout");
        AutomationProperties.SetName(CloseButton, StringTable.Get("Close"));
        ToolTipService.SetToolTip(CloseButton, StringTable.Get("Close"));
        _initialized = true;
        ShowCategory("general");
    }

    public void ShowSection(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        tag = NormalizeCategory(tag);
        foreach (var raw in CategoryList.Items)
        {
            if (raw is ListViewItem item && string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            {
                CategoryList.SelectedItem = item;
                ShowCategory(tag);
                return;
            }
        }
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (CategoryList.SelectedItem is ListViewItem { Tag: string tag })
        {
            ShowCategory(tag);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ShowCategory(string tag)
    {
        _pendingCategory = NormalizeCategory(tag);
        // Building a settings page mutates the visual tree. Doing that inside
        // ListView.SelectionChanged re-enters WinUI layout and can take the
        // process down with STATUS_STOWED_EXCEPTION.
        if (!DispatcherQueue.TryEnqueue(ShowPendingCategory))
        {
            ShowPendingCategory();
        }
    }

    private void ShowPendingCategory()
    {
        var tag = _pendingCategory;
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        if (!_sections.TryGetValue(tag, out var section))
        {
            try
            {
                section = CreateSection(tag);
            }
            catch (Exception error)
            {
                section = new PageLoadErrorPage(tag, error.ToString());
            }

            _sections[tag] = section;
        }

        if (!ReferenceEquals(SectionHost.Content, section))
        {
            SectionHost.Content = section;
        }
    }

    private static string NormalizeCategory(string tag) => tag switch
    {
        "multitasking" => "general",
        "shortcuts" => "keyboard",
        _ => tag,
    };

    private static UIElement CreateSection(string tag) => tag switch
    {
        "appearance" => new AppearancePage(),
        "files-folders" => new FilesAndFoldersSettingsPage(),
        "keyboard" => new ShortcutsSettingsPage(),
        "search" => new SearchSettingsPage(),
        "tags" => new TagManagementPage(),
        "advanced" => new AdvancedSettingsPage(),
        "about" => new AboutPage(),
        _ => new GeneralPage(),
    };
}
