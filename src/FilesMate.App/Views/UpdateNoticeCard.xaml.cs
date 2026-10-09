using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using FilesMate.App.Theming;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Views;

public sealed partial class UpdateNoticeCard : UserControl
{
    private readonly List<(TextBlock Text, double Size)> _texts = [];
    public UpdateNoticeCard()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "UnifiedAnnouncementCard");
        AppTypography.Track(this);
    }
    public event EventHandler? UpdateRequested;
    public event EventHandler? DismissRequested;
    public Button CloseControl => AnnouncementCloseButton;
    public Button PrimaryControl => AnnouncementPrimaryAction;
    public void SetVersion(string version) => AnnouncementVersion.Text = version;
    public void ShowDemoLabel() { }
    public void SetAvailable(string version)
    {
        Reset(Loc.Get("Update_Available"), version, Loc.Get(FilesMate.Core.Configuration.RuntimeProfile.IsPortable ? "Portable_UpdateAction" : "Update_Install"));
        AnnouncementBody.Children.Add(Text(Loc.Get(FilesMate.Core.Configuration.RuntimeProfile.IsPortable ? "Portable_ManualUpdate" : "Update_PreserveHint"), 14, "AvailableUpdateDescription", secondary: true));
    }
    public void SetHighlights(string version)
    {
        Reset(Loc.Get("Tour_HighlightsTitle"), Loc.Format("Tour_HighlightsVersion", version), Loc.Get("Tour_StartUsing"));
        foreach (var (topic, glyph) in new[] { ("Recursive", "\uE8B7"), ("Filters", "\uE71C"), ("Preview", "\uE8A7") })
        {
            var item = new Grid { ColumnSpacing = 12 };
            item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new FontIcon { Glyph = glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            ThemeResources.Bind(icon, FontIcon.ForegroundProperty, "FilesMate.Text.SecondaryBrush");
            item.Children.Add(icon);
            var copy = new StackPanel { Spacing = 5 };
            copy.Children.Add(Text(Loc.Get("Tour_Highlights" + topic + "Title"), 16, "Highlights" + topic + "Title", bold: true));
            copy.Children.Add(Text(Loc.Get("Tour_Highlights" + topic + "Body"), 14, "Highlights" + topic + "Body", secondary: true));
            Grid.SetColumn(copy, 1); item.Children.Add(copy); AnnouncementBody.Children.Add(item);
        }
    }
    private void Reset(string title, string version, string action)
    {
        AnnouncementBody.Children.Clear(); _texts.Clear();
        AnnouncementTitle.Text = title; AnnouncementTitle.FontSize = 20;
        AnnouncementVersion.Text = version; AnnouncementVersion.FontSize = 12;
        AnnouncementPrimaryAction.Content = action; AnnouncementPrimaryAction.FontSize = 14;
        AutomationProperties.SetName(this, title);
    }
    private TextBlock Text(string value, double size, string id, bool bold = false, bool secondary = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
        AutomationProperties.SetAutomationId(text, id);
        ThemeResources.Bind(text, TextBlock.ForegroundProperty, secondary ? "FilesMate.Text.SecondaryBrush" : "FilesMate.Text.PrimaryBrush");
        _texts.Add((text, size)); return text;
    }
    public void Constrain(double availableWidth, double availableHeight)
    {
        Width = Math.Max(0, Math.Min(520, availableWidth));
        MaxHeight = Math.Max(0, availableHeight);
        var chrome = 80 + Math.Max(AnnouncementHeader.ActualHeight, 50) + Math.Max(AnnouncementPrimaryAction.ActualHeight, 36);
        AnnouncementScroll.MaxHeight = Math.Max(0, availableHeight - chrome);
    }
    internal void SetDiagnosticTextScale(double scale)
    {
        if (!FilesMate.Core.Configuration.RuntimeProfile.IsIsolated) return;
        scale = Math.Clamp(scale, 1, 2);
        AnnouncementTitle.FontSize = 20 * scale; AnnouncementVersion.FontSize = 12 * scale;
        AnnouncementPrimaryAction.FontSize = 14 * scale;
        foreach (var (text, size) in _texts) text.FontSize = size * scale;
    }
    private void Install_Click(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);
    private void Dismiss_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke(this, EventArgs.Empty);
}
