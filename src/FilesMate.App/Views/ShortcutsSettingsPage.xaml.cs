using FilesMate.App.Controls.Settings;
using FilesMate.App.Localization;
using FilesMate.App.Shortcuts;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class ShortcutsSettingsPage : UserControl
{
    private readonly Dictionary<ShortcutAction, ShortcutEditorRow> _rows = [];

    public ShortcutsSettingsPage()
    {
        InitializeComponent();
        Heading.Text = StringTable.Get("SettingsKeyboard");
        Lead.Text = StringTable.Get("Shortcut_Lead");
        ShortcutsHeader.Text = StringTable.Get("ShortcutsTitle");
        ShortcutsLead.Text = StringTable.Get("Shortcut_Lead");
        RestoreDefaultsLabel.Text = StringTable.Get("Shortcut_RestoreDefaults");
        BuildRows();
        Loaded += (_, _) =>
        {
            App.ShortcutsChanged -= App_ShortcutsChanged;
            App.ShortcutsChanged += App_ShortcutsChanged;
            RefreshRows();
        };
        Unloaded += ShortcutsSettingsPage_Unloaded;
    }

    private void BuildRows()
    {
        ShortcutRows.Children.Clear();
        _rows.Clear();
        for (var index = 0; index < ShortcutDefaults.Definitions.Count; index++)
        {
            var definition = ShortcutDefaults.Definitions[index];
            var row = new ShortcutEditorRow();
            row.Configure(
                definition.Action,
                StringTable.Get(definition.LabelKey),
                App.Shortcuts[definition.Action],
                showDivider: index < ShortcutDefaults.Definitions.Count - 1);
            row.GestureSubmitted += Row_GestureSubmitted;
            _rows[definition.Action] = row;
            ShortcutRows.Children.Add(row);
        }
    }

    private async void Row_GestureSubmitted(object? sender, ShortcutGesture gesture)
    {
        if (sender is not ShortcutEditorRow row)
        {
            return;
        }

        SaveErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var conflict = await App.UpdateShortcutAsync(row.Action, gesture).ConfigureAwait(true);
            if (conflict is { } conflictAction)
            {
                var conflictName = StringTable.Get(ShortcutDefaults.For(conflictAction).LabelKey);
                row.SetGesture(App.Shortcuts[row.Action]);
                row.ShowConflict(conflictName);
                return;
            }

            row.SetGesture(App.Shortcuts[row.Action]);
        }
        catch
        {
            row.SetGesture(App.Shortcuts[row.Action]);
            SaveErrorText.Text = StringTable.Get("Shortcut_SaveFailed");
            SaveErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveErrorText.Visibility = Visibility.Collapsed;
        try
        {
            await App.ResetShortcutsAsync().ConfigureAwait(true);
            RefreshRows();
        }
        catch
        {
            SaveErrorText.Text = StringTable.Get("Shortcut_SaveFailed");
            SaveErrorText.Visibility = Visibility.Visible;
        }
    }

    private void App_ShortcutsChanged(object? sender, EventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        foreach (var (action, row) in _rows)
        {
            row.SetGesture(App.Shortcuts[action]);
        }
    }

    private void ShortcutsSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ShortcutsChanged -= App_ShortcutsChanged;
        App.IsShortcutCaptureActive = false;
    }
}
