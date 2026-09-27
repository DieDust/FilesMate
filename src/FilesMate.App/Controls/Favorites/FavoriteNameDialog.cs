using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Controls.Favorites;

internal sealed class FavoriteNameDialog : ContentDialog
{
    public FavoriteNameDialog(string title, string initial, Func<string, Task> save)
    {
        Resources["ContentDialogMinHeight"] = 0d;
        Resources["ContentDialogMinWidth"] = 0d;
        Resources["ContentDialogPadding"] = new Thickness(20);
        DefaultButton = ContentDialogButton.None;
        AutomationProperties.SetName(this, title);
        var input = new TextBox { Text = initial, PlaceholderText = Loc.Get("Favorites_NameHint"), MaxLength = 120 };
        AutomationProperties.SetAutomationId(input, "FavoriteGroupName");
        AutomationProperties.SetName(input, Loc.Get("Favorites_Name"));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, FontSize = 12 };
        AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var panel = new StackPanel { Spacing = 12, Width = 320 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(input);
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        var confirm = new Button { Content = Loc.Get("Confirm"), MinWidth = 80, Height = 32, Padding = new Thickness(12, 4, 12, 4), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancel = new Button { Content = Loc.Get("Cancel"), MinWidth = 80, Height = 32, Padding = new Thickness(12, 4, 12, 4) };
        AutomationProperties.SetAutomationId(confirm, "FavoriteGroupConfirm");
        AutomationProperties.SetAutomationId(cancel, "FavoriteGroupCancel");
        actions.Children.Add(confirm); actions.Children.Add(cancel);
        panel.Children.Add(actions);
        Content = panel;
        var busy = false;
        async Task SaveAsync()
        {
            if (busy) return;
            busy = true;
            input.IsEnabled = confirm.IsEnabled = cancel.IsEnabled = false;
            error.Visibility = Visibility.Collapsed;
            try { await save(input.Text); busy = false; Hide(); }
            catch (Exception failure) { error.Text = failure.Message; error.Visibility = Visibility.Visible; }
            finally { busy = false; input.IsEnabled = confirm.IsEnabled = cancel.IsEnabled = true; }
        }
        confirm.Click += async (_, _) => await SaveAsync();
        cancel.Click += (_, _) => Hide();
        input.KeyDown += async (_, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; await SaveAsync(); } };
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Opened += (_, _) =>
        {
            panel.Width = Math.Min(320, Math.Max(160, XamlRoot.Size.Width - 80));
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
        };
    }
}
