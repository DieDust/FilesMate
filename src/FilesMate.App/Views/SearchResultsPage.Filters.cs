using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Globalization;
using Windows.Storage.Pickers;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Views;

public sealed partial class SearchResultsPage
{
    private bool _syncingFilters;
    private static readonly string[] DateValues = ["", "today", "yesterday", "7days", "30days", "90days"];
    private void InitializeFilterControls()
    {
        FlyoutTheme.FollowHost(ScopeFlyout); FlyoutTheme.FollowHost(SizeFlyout);
        FlyoutTheme.FollowHost(TypeFlyout); FlyoutTheme.FollowHost(DateFlyout);
        ScopeHeading.Text = Loc.Get("SearchPage_Scope");
        SizeHeading.Text = Loc.Get("SearchPage_Size");
        AllLocationsButton.Content = Loc.Get("SearchPage_AllLocations");
        BrowseScopeButton.Content = Loc.Get("SearchPage_Browse");
        ApplyScopeButton.Content = ApplySizeButton.Content = ApplyTypeButton.Content = Loc.Get("SearchPage_Apply");
        SizeBox.Header = Loc.Get("SearchPage_CustomSize");
        MinimumSize.Header = Loc.Get("SearchPage_Minimum"); MaximumSize.Header = Loc.Get("SearchPage_Maximum");
        MinimumSize.PlaceholderText = MaximumSize.PlaceholderText = Loc.Get("SearchPage_NoLimit");
        AutomationProperties.SetName(SizeUnit, Loc.Get("SearchPage_Unit"));
        FormatsHeading.Text = Loc.Get("SearchPage_Formats");
        DateHeading.Text = Loc.Get("SearchPage_Modified");
        var dateIndex = 0;
        foreach (var key in new[] { "AnyDate", "Today", "Yesterday", "Last7Days", "Last30Days", "Last90Days", "CustomDates" })
        {
            DatePresetBox.Items.Add(Loc.Get("SearchPage_" + key));
            var preset = new Button { Content = Loc.Get("SearchPage_" + key), Tag = dateIndex,
                HorizontalAlignment = HorizontalAlignment.Stretch, Height = 32,
                Style = (Style)Resources["SearchFilterButtonStyle"] };
            Grid.SetRow(preset, dateIndex / 2); Grid.SetColumn(preset, dateIndex % 2);
            if (dateIndex == 6) Grid.SetColumnSpan(preset, 2);
            preset.Click += (_, _) => { DatePresetBox.SelectedIndex = (int)preset.Tag; if ((int)preset.Tag < 6) DateFlyout.Hide(); };
            DatePresets.Children.Add(preset);
            dateIndex++;
        }
        StartDateBox.Language = EndDateBox.Language = CultureInfo.CurrentUICulture.Name;
        StartDateBox.Header = StartDateBox.PlaceholderText = Loc.Get("SearchPage_StartDate");
        EndDateBox.Header = EndDateBox.PlaceholderText = Loc.Get("SearchPage_EndDate");
        foreach (var (label, extensions) in new[] { ("PDF", "pdf"), ("Word", "doc;docx;odt"), ("Excel", "xls;xlsx;csv"),
            ("PowerPoint", "ppt;pptx"), ("TXT", "txt;md"), (Loc.Get("SearchRankImage"), "png;jpg;jpeg;gif;webp;svg"),
            (Loc.Get("Category_Media"), "mp3;wav;flac;mp4;mkv;mov"), (Loc.Get("SearchRankArchive"), "zip;7z;rar;tar;gz") })
        {
            var button = new ToggleButton { Style = (Style)Resources["FilterChipStyle"], Content = label, Tag = extensions,
                HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Height = 32,
                FontSize = 11, Padding = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(8) };
            Grid.SetColumn(button, FormatChoices.Children.Count % 4); Grid.SetRow(button, FormatChoices.Children.Count / 4);
            AutomationProperties.SetName(button, label);
            button.Click += Format_Click; FormatChoices.Children.Add(button);
        }
        var sizeIndex = 0;
        foreach (var (label, value) in new[] { (Loc.Get("SearchPage_AnySize"), ""), ("< 1 MB", "<1mb"),
            ("1 – 100 MB", "1mb..100mb"), ("> 100 MB", ">100mb"), ("> 1 GB", ">1gb") })
        {
            var button = new Button { Content = label, Tag = value, HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 32, Style = (Style)Resources["SearchFilterButtonStyle"] };
            Grid.SetRow(button, sizeIndex == 0 ? 0 : (sizeIndex + 1) / 2);
            Grid.SetColumn(button, sizeIndex == 0 ? 0 : (sizeIndex - 1) % 2);
            if (sizeIndex == 0) Grid.SetColumnSpan(button, 2);
            button.Click += async (_, _) => { SizeBox.Text = value; SyncSizeInputs(); HideFilterPopups(); await SearchAsync(); };
            SizePresets.Children.Add(button);
            sizeIndex++;
        }
    }

    private void SyncVisualFilters()
    {
        _syncingFilters = true;
        try
        {
            var extensions = ExtensionSet();
            foreach (var button in FormatChoices.Children.OfType<ToggleButton>())
                button.IsChecked = ((string)button.Tag).Split(';').Any(extensions.Contains);
            var index = Array.IndexOf(DateValues, DateBox.Text);
            DatePresetBox.SelectedIndex = index >= 0 ? index : 6;
            DateRangePanel.Visibility = index < 0 ? Visibility.Visible : Visibility.Collapsed;
            if (index < 0)
            {
                var range = DateBox.Text.Split("..");
                if (DateTime.TryParseExact(range[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) StartDateBox.Date = new DateTimeOffset(start);
                if (DateTime.TryParseExact(range[^1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) EndDateBox.Date = new DateTimeOffset(end);
            }
            SyncSizeInputs();
        }
        finally { _syncingFilters = false; }
    }

    private HashSet<string> ExtensionSet() => ExtensionsBox.Text.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);

#if FILESMATE_UI_TEST
    internal void ApplyFormatForSmoke(ToggleButton button) => Format_Click(button, new RoutedEventArgs());
#endif
    private async void Format_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingFilters || sender is not ToggleButton button) return;
        var values = ExtensionSet();
        foreach (var value in ((string)button.Tag).Split(';'))
            if (button.IsChecked == true) values.Add(value); else values.Remove(value);
        ExtensionsBox.Text = string.Join(';', values);
        await SearchAsync();
    }

    private async void DatePreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters || !_ready) return;
        var custom = DatePresetBox.SelectedIndex == DateValues.Length;
        DateRangePanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom)
        {
            _syncingFilters = true;
            StartDateBox.Date ??= DateTimeOffset.Now.Date.AddDays(-6);
            EndDateBox.Date ??= DateTimeOffset.Now.Date;
            _syncingFilters = false;
            DateBox.Text = StartDateBox.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".." + EndDateBox.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await SearchAsync();
            return;
        }
        DateBox.Text = DateValues[Math.Max(0, DatePresetBox.SelectedIndex)];
        await SearchAsync();
    }

    private async void DateRange_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_syncingFilters || !_ready) return;
        if (StartDateBox.Date is not { } start || EndDateBox.Date is not { } end) return;
        if (end.Date < start.Date) { ShowError(Loc.Get("SearchPage_DateOrder")); return; }
        DateBox.Text = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".." + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await SearchAsync();
    }

    private async void MatchOption_Click(object sender, RoutedEventArgs e) { if (_ready) await SearchAsync(); }

    private void SyncSizeInputs()
    {
        MinimumSize.Value = MaximumSize.Value = double.NaN;
        var value = SizeBox.Text.ToLowerInvariant();
        var unit = value.EndsWith("gb") ? "gb" : value.EndsWith("mb") ? "mb" : value.EndsWith("kb") ? "kb" : "b";
        SizeUnit.SelectedIndex = Array.IndexOf(new[] { "b", "kb", "mb", "gb" }, unit);
        var range = value.Split("..");
        double Number(string text) => double.TryParse(text.TrimStart('>', '<', '=').TrimEnd('k','m','g','b'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
        if (range.Length == 2) { MinimumSize.Value = Number(range[0]); MaximumSize.Value = Number(range[1]); }
        else if (value.StartsWith('<')) MaximumSize.Value = Number(value);
        else if (value.StartsWith('>')) MinimumSize.Value = Number(value);
    }

    private async void ApplySize_Click(object sender, RoutedEventArgs e)
    {
        var min = MinimumSize.Value; var max = MaximumSize.Value;
        if (!double.IsNaN(min) && !double.IsNaN(max) && min > max) { ShowError(Loc.Get("SearchPage_SizeOrder")); return; }
        var unit = (string)SizeUnit.SelectedItem;
        string Value(double value) => value.ToString("0.########", CultureInfo.InvariantCulture) + unit;
        SizeBox.Text = double.IsNaN(min) ? double.IsNaN(max) ? "" : "<=" + Value(max)
            : double.IsNaN(max) ? ">=" + Value(min) : Value(min) + ".." + Value(max);
        HideFilterPopups(); await SearchAsync();
    }
    private void HideFilterPopups() { FilterFlyout.Hide(); ScopeFlyout.Hide(); SizeFlyout.Hide(); TypeFlyout.Hide(); DateFlyout.Hide(); }
    private void UpdateFilterCaptions()
    {
        var scope = Request.Scope;
        var leaf = scope is null ? "" : System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(scope));
        ScopeCaption.Text = scope is null ? Loc.Get("SearchPage_AllLocations") : string.IsNullOrEmpty(leaf) ? scope : leaf;
        SizeCaption.Text = SizeCaptionFor(Request.Size);
        var extensions = ExtensionSet();
        var formats = FormatChoices.Children.OfType<ToggleButton>().ToArray();
        foreach (var format in formats) format.IsChecked = ((string)format.Tag).Split(';').Any(extensions.Contains);
        var chosen = formats.Where(format => format.IsChecked == true).Select(format => (string)format.Content).ToArray();
        TypeCaption.Text = chosen.Length > 0 ? chosen[0] + (chosen.Length > 1 ? $" +{chosen.Length - 1}" : "")
            : extensions.Count > 0 ? "." + extensions.First() + (extensions.Count > 1 ? $" +{extensions.Count - 1}" : "")
            : (CategoryBox.SelectedItem as CategoryOption)?.Label ?? Loc.Get("SearchPage_AllTypes");
        var dateIndex = Array.IndexOf(DateValues, Request.Modified);
        DateCaption.Text = dateIndex >= 0 ? (string)DatePresetBox.Items[dateIndex] : Request.Modified.Replace("..", " – ");
        foreach (var preset in SizePresets.Children.OfType<Button>())
            preset.Style = (Style)Resources[(string)preset.Tag == Request.Size ? "ActiveSearchFilterStyle" : "SearchFilterButtonStyle"];
        foreach (var preset in DatePresets.Children.OfType<Button>())
            preset.Style = (Style)Resources[(int)preset.Tag == (dateIndex >= 0 ? dateIndex : 6) ? "ActiveSearchFilterStyle" : "SearchFilterButtonStyle"];
        TypeButton.Style = (Style)Resources[Request.Extensions.Length > 0 || Request.CategoryId != "All" ? "ActiveSearchFilterStyle" : "SearchFilterButtonStyle"];
        DateButton.Style = (Style)Resources[Request.Modified.Length > 0 ? "ActiveSearchFilterStyle" : "SearchFilterButtonStyle"];
        ScopeButton.Style = (Style)Resources[scope is not null ? "ActiveSearchFilterStyle" : "SearchFilterButtonStyle"];
        AutomationProperties.SetName(TypeButton, Loc.Get("SearchPage_Category") + ": " + TypeCaption.Text);
        AutomationProperties.SetName(DateButton, Loc.Get("SearchPage_Modified") + ": " + DateCaption.Text);
        ToolTipService.SetToolTip(TypeButton, TypeCaption.Text + (Request.Extensions.Length > 0 ? "\n" + Request.Extensions : ""));
        ToolTipService.SetToolTip(DateButton, Loc.Get("SearchPage_Modified") + ": " + DateCaption.Text);
        AutomationProperties.SetName(ScopeButton, Loc.Get("SearchPage_Scope") + ": " + (scope ?? ScopeCaption.Text));
        ToolTipService.SetToolTip(ScopeButton, scope ?? ScopeCaption.Text);
        AutomationProperties.SetName(SizeButton, Loc.Get("SearchPage_Size") + ": " + SizeCaption.Text);
        ToolTipService.SetToolTip(SizeButton, Loc.Get("SearchPage_Size") + ": " + SizeCaption.Text);
        SizeButton.Style = (Style)Resources[Request.Size.Length > 0 ? "ActiveSearchFilterStyle" : "SearchFilterButtonStyle"];
    }
    private static string SizeCaptionFor(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Loc.Get("SearchPage_AnySize");
        var match = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"^(>=|<=|>|<|=)?\s*(\d+(?:\.\d+)?)\s*([kmgt]?b)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return value.Replace("..", " – ").ToUpperInvariant();
        var comparison = match.Groups[1].Value.Replace(">=", "≥").Replace("<=", "≤");
        var unit = match.Groups[3].Value.ToUpperInvariant();
        var number = match.Groups[2].Value;
        if (unit.Length == 0 && double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bytes))
        {
            unit = bytes >= 1048576 ? "MB" : bytes >= 1024 ? "KB" : "B";
            number = (bytes / (unit == "MB" ? 1048576 : unit == "KB" ? 1024 : 1)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
        return comparison + (comparison.Length > 0 ? " " : "") + number + " " + unit;
    }
    private async void AllLocations_Click(object sender, RoutedEventArgs e)
    { ScopeBox.Text = ""; HideFilterPopups(); await SearchAsync(); }
    private async void BrowseScope_Click(object sender, RoutedEventArgs e)
    {
        ScopeFlyout.Hide();
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowForElement(this)!.NativeHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null || _disposed) return;
        ScopeBox.Text = folder.Path; await SearchAsync();
    }
}
