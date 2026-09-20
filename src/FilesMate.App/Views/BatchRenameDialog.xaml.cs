using FilesMate.App.Localization;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class BatchRenameDialog : UserControl
{
    private const int PreviewDebounceMilliseconds = 180;
    private IReadOnlyList<string> _sources = [];
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _debounce;
    private readonly bool _ready;

    public BatchRenameDialog()
    {
        InitializeComponent();
        _debounce = DispatcherQueue.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(PreviewDebounceMilliseconds);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => RefreshPreview();
        ModeBox.Header = T("RenameOperation");
        foreach (var key in new[] { "RenameModeReplace", "RenameModeNumber", "RenameModeAffix", "RenameModeCase" }) ModeBox.Items.Add(T(key));
        ModeBox.SelectedIndex = 0;
        FindBox.Header = T("RenameFind");
        ReplaceBox.Header = T("RenameReplace");
        ReplaceBox.PlaceholderText = T("RenameRemoveHint");
        MatchCaseBox.Content = T("RenameMatchCase");
        BaseNameBox.Header = T("RenameBaseName");
        BaseNameBox.PlaceholderText = T("RenameBaseExample");
        PrefixBox.Header = T("RenamePrefix");
        SuffixBox.Header = T("RenameSuffix");
        CaseBox.Items.Add(T("RenameUpper"));
        CaseBox.Items.Add(T("RenameLower"));
        CaseBox.SelectedIndex = 0;
        StartBox.Header = T("RenameStart");
        DigitsBox.Header = T("RenameDigits");
        foreach (var value in new[] { "1", "01", "001", "0001" }) DigitsBox.Items.Add(value);
        DigitsBox.SelectedIndex = 2;
        ExtensionBox.Content = T("RenameIncludeExtension");
        OriginalHeader.Text = T("RenameOriginal");
        NewHeader.Text = T("RenameNew");
        FooterHint.Text = T("RenameUndoHint");
        HelpTitle.Text = T("RenameHelpTitle");
        ResetRulesButton.Content = T("RenameResetRules");
        _ready = true;
        ActualThemeChanged += (_, _) => RefreshPreview();
        Unloaded += (_, _) => _debounce.Stop();
    }

    public BatchRenamePlan CurrentPlan { get; private set; } = new([]);
    public bool CanApply => CurrentPlan.IsValid && CurrentPlan.Entries.Any(e => e.RequiresRename);
    public event EventHandler? PlanChanged;

    public void SetSources(IReadOnlyList<string> sources)
    {
        _sources = sources ?? [];
        _directories.Clear();
        foreach (var path in _sources.Where(Directory.Exists)) _directories.Add(Path.GetFullPath(path));
        RefreshPreview();
    }

    public ContentDialog CreateDialog(XamlRoot root)
    {
        var dialog = new RenameContentDialog
        {
            Title = T("Command_BatchRename"), Content = this, XamlRoot = root,
            PrimaryButtonText = T("Command_BatchRename"), CloseButtonText = T("Cancel"),
            DefaultButton = ContentDialogButton.None, IsPrimaryButtonEnabled = CanApply,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1024d;
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent) && accent is Style accentStyle)
            dialog.PrimaryButtonStyle = accentStyle;
        void UpdateSize()
        {
            Width = Math.Max(240, Math.Min(940, root.Size.Width - 96));
            Height = Math.Max(180, Math.Min(500, root.Size.Height - 210));
            var compact = Width < 500;
            RulesColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(Width < 780 ? 216 : 252);
            PreviewRow.Height = compact ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
            RulesRow.Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            Grid.SetColumnSpan(RulesPanel, compact ? 2 : 1);
            ModeHint.Visibility = Height < 420 || compact ? Visibility.Collapsed : Visibility.Visible;
            HelpTitle.Visibility = ModeHint.Visibility;
            FooterHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetColumn(PreviewPane, compact ? 0 : 1);
            Grid.SetRow(PreviewPane, compact ? 1 : 0);
            Grid.SetColumnSpan(PreviewPane, compact ? 2 : 1);
        }
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateSize();
        void Changed(object? sender, EventArgs args)
        {
            dialog.IsPrimaryButtonEnabled = CanApply;
            var count = CurrentPlan.Entries.Count(e => e.RequiresRename && e.Error is null);
            dialog.PrimaryButtonText = count > 0 ? string.Format(T("RenameApplyCount"), count) : T("Command_BatchRename");
        }
        UpdateSize();
        PlanChanged += Changed;
        dialog.Opened += (_, _) => root.Changed += RootChanged;
        dialog.Closed += (_, _) => { root.Changed -= RootChanged; PlanChanged -= Changed; _debounce.Stop(); };
        dialog.PrimaryButtonClick += (_, args) => { RefreshPreview(); args.Cancel = !CanApply; };
        return dialog;
    }

    private static string T(string key) => StringTable.Get(key);
    private sealed class RenameContentDialog : ContentDialog
    {
        public RenameContentDialog() => DefaultStyleKey = typeof(ContentDialog);

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // Reapply after template/theme changes as well as first opening.
            // Keep native button semantics, focus handling and dialog results.
            if (GetTemplateChild("CommandSpace") is Grid commands)
            {
                commands.HorizontalAlignment = HorizontalAlignment.Right;
                commands.MaxWidth = 360;
            }
        }
    }

    private void ResetRules_Click(object sender, RoutedEventArgs e)
    {
        FindBox.Text = ReplaceBox.Text = BaseNameBox.Text = PrefixBox.Text = SuffixBox.Text = "";
        MatchCaseBox.IsChecked = ExtensionBox.IsChecked = false;
        StartBox.Value = 1;
        DigitsBox.SelectedIndex = 2;
        CaseBox.SelectedIndex = 0;
        ModeBox.SelectedIndex = 0;
        RefreshPreview();
    }
    private void RuleChanged(TextBox sender, TextBoxTextChangingEventArgs e) => QueuePreview();
    private void OptionChanged(object sender, RoutedEventArgs e) => QueuePreview();
    private void NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) => QueuePreview();

    private void QueuePreview()
    {
        if (!_ready) return;
        // Disable execution of the previous plan while the next preview is pending.
        CurrentPlan = new([]);
        PlanChanged?.Invoke(this, EventArgs.Empty);
        _debounce.Stop();
        _debounce.Start();
    }

    public void RefreshPreview()
    {
        if (!_ready) return;
        _debounce.Stop();
        var mode = ModeBox.SelectedIndex;
        ReplaceOptions.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        BaseNameBox.Visibility = NumberOptions.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        AffixOptions.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        CaseBox.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
        ModeHint.Text = T(mode switch { 1 => "RenameHelpNumber", 2 => "RenameHelpAffix", 3 => "RenameHelpCase", _ => "RenameHelpReplace" });
        ToolTipService.SetToolTip(ModeBox, ModeHint.Text);
        var rule = new BatchRenameRule
        {
            Find = mode == 0 ? FindBox.Text : "", Replace = ReplaceBox.Text,
            CaseSensitive = MatchCaseBox.IsChecked == true,
            BaseName = mode == 1 ? BaseNameBox.Text : null,
            Prefix = mode == 2 ? PrefixBox.Text : "", Suffix = mode == 2 ? SuffixBox.Text : "",
            NumberStart = mode == 1 && double.IsFinite(StartBox.Value) ? (int)StartBox.Value : null,
            NumberWidth = DigitsBox.SelectedIndex + 1,
            Uppercase = mode == 3 && CaseBox.SelectedIndex == 0,
            Lowercase = mode == 3 && CaseBox.SelectedIndex == 1,
            IncludeExtension = ExtensionBox.IsChecked == true,
        };
        CurrentPlan = BatchRenamePlanner.Plan(_sources, rule, Path.Exists, _directories.Contains);
        if (mode == 1 && (string.IsNullOrWhiteSpace(BaseNameBox.Text) || !double.IsFinite(StartBox.Value) || StartBox.Value != Math.Truncate(StartBox.Value)))
            CurrentPlan = new(CurrentPlan.Entries.Select(e => e with { Error = T("RenameNumberRequired") }).ToArray());
        var changes = CurrentPlan.Entries.Count(e => e.RequiresRename && e.Error is null);
        var errors = CurrentPlan.Entries.Count(e => e.Error is not null);
        SummaryText.Text = string.Format(T("RenameSummary"), CurrentPlan.Entries.Count, changes, errors);
        PreviewList.ItemsSource = CurrentPlan.Entries.Select((e, index) => new RenamePreviewRow(
            Path.GetFileName(e.Source), Path.GetFileName(e.Target), e.Source, e.Target,
            Status(e), e.Error is null ? Visibility.Visible : Visibility.Collapsed,
            e.Error is null ? Visibility.Collapsed : Visibility.Visible,
            (Style)Resources[index % 2 == 0 ? "RenameEvenRow" : "RenameOddRow"])).ToArray();
        PlanChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Status(BatchRenameEntry e)
    {
        if (e.Error is null) return T(e.RequiresRename ? "RenameChanged" : "RenameUnchanged");
        if (e.Error.StartsWith("Multiple items", StringComparison.Ordinal)) return T("RenameDuplicate");
        if (e.Error.StartsWith("The target", StringComparison.Ordinal)) return T("RenameExists");
        if (e.Error.Contains("valid file name", StringComparison.Ordinal) || e.Error.Contains("cannot be empty", StringComparison.Ordinal)) return T("Error_InvalidName");
        return e.Error;
    }
}

public sealed record RenamePreviewRow(string Original, string NewName, string Source, string Target, string Status, Visibility NormalVisibility, Visibility ErrorVisibility, Style RowStyle);
