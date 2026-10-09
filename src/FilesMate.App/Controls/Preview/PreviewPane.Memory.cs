using FilesMate.App.Icons;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Preview;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Controls.Preview;

public sealed partial class PreviewPane
{
    // Shares the production text presenter and icon catalogue. No service,
    // path, metadata reader, browser, media decoder or external preview exists
    // on this deliberately separate, memory-only binding path.
    internal void BindMemoryText(FileEntryCore entry, string content)
    {
        if (_service is not null) throw new InvalidOperationException("Memory preview must not attach a file service.");
        CancelAndClear();
        ItemHeader.Visibility = Visibility.Visible;
        ItemName.Text = entry.Name;
        ItemType.Text = StringTable.Get("Column_Type");
        ShellIconBinder.Bind(ItemIcon, ItemGlyph, entry, null, 48);
        PropertyList.Visibility = Visibility.Visible;
        LocationValue.Text = "Project";
        ModifiedValue.Text = PreviewDetails.Modified(new DateTimeOffset(entry.ModifiedUtcTicks, TimeSpan.Zero), App.ExplorerPreferences.DateFormat);
        CreatedValue.Text = PreviewDetails.Modified(new DateTimeOffset(entry.CreatedUtcTicks, TimeSpan.Zero), App.ExplorerPreferences.DateFormat);
        SizeRow.Visibility = Visibility.Visible;
        SizeValue.Text = DriveCapacity.FormatBytes((long)entry.Size);
        ItemsRow.Visibility = AttributesRow.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        CompatiblePreviewButton.Visibility = Visibility.Collapsed;
        RenderText(content, false);
        _hasContent = true;
        _showInformation = false;
        ApplyPaneMode();
    }

    private void RenderText(string content, bool truncated)
    {
        _visibleTextLines.Clear();
        _textLines = (truncated ? content + Environment.NewLine + Environment.NewLine + StringTable.Get("PreviewTruncated") : content)
            .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        AppendTextLines();
        _textScroll?.ChangeView(0, 0, null, true);
        TextHost.Visibility = Visibility.Visible;
    }
}
