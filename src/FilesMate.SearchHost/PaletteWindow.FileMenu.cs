using Loc = FilesMate.App.Localization.StringTable;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FilesMate.App.Commands;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Services;
using FilesMate.Search;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private void OpenResultMenu(bool keyboard)
    {
        if (_pending || _resultMenu?.IsOpen == true) return;
        ClearPreview();
        var selected = SelectedRows();
        FrameworkElement anchor = Results;
        if (keyboard && Results.SelectedItem is { } current)
        {
            Results.ScrollIntoView(current);
            Results.UpdateLayout();
            anchor = Results.ItemContainerGenerator.ContainerFromItem(current) as FrameworkElement ?? Results;
        }
        var menu = new ContextMenu
        {
            PlacementTarget = anchor, Resources = Resources,
            Placement = keyboard ? PlacementMode.Bottom : PlacementMode.MousePoint,
            MaxHeight = SystemParameters.WorkArea.Height - 32
        };
        _resultMenu = menu;
        menu.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        // The Apps key opens on key-down. Its matching key-up must not let
        // WPF's built-in context-menu gesture immediately toggle it closed.
        menu.PreviewKeyUp += (_, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key is System.Windows.Input.Key.Apps or System.Windows.Input.Key.F10) e.Handled = true;
        };
        TextBlock Glyph(string glyph) => new() { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16, Foreground = (Brush)Resources["Accent"], VerticalAlignment = VerticalAlignment.Center };
        void Invoke(string command)
        {
            menu.IsOpen = false;
            // An asynchronous search may replace rows while a menu is open.
            if (_pending || !selected.Select(row => row.Path).SequenceEqual(SelectedRows().Select(row => row.Path)))
            { ActionError(Loc.Get("Search_ResultsChanged")); return; }
            InvokeFileCommand(command);
        }
        MenuItem Item(string title, string glyph, string command, string? shortcut = null, bool enabled = true)
        {
            var item = new MenuItem { Header = title, Icon = Glyph(glyph), InputGestureText = shortcut ?? "", IsEnabled = enabled, Tag = command };
            item.Click += (_, e) => { e.Handled = true; Invoke(command); };
            if (Enum.TryParse<AppCommandId>(command, out var id) && CompactMateSession.RequiresExternalProvider(id))
                _ = ApplyArchiveProviderAsync(item, title, enabled);
            return item;
        }
        void AddSeparator()
        {
            if (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is not Separator)
                menu.Items.Add(new Separator { Style = (Style)Resources[typeof(Separator)] });
        }
        if (selected.Any(row => row.IsApplication))
        {
            menu.Items.Add(Item(Loc.Get("Command_Open"), "\uE8E5", "Open", "Enter"));
            if (selected.All(row => row.CanLocate))
            {
                menu.Items.Add(Item(Loc.Get("RevealFile"), "\uE8B7", "Reveal", "Ctrl+Enter"));
                menu.Items.Add(Item(Loc.Get("Command_CopyPath"), "\uE71B", "CopyPath", "Ctrl+Shift+C"));
                if (selected.Length == 1) menu.Items.Add(Item(Loc.Get("Preview_Attributes"), "\uE946", "Properties", "Alt+Enter"));
            }
            if (selected.All(row => row.Hit.Application?.ShortcutPath is not null))
                menu.Items.Add(Item(Loc.Get("Shortcut_DeleteMenu"), "\uE74D", "Recycle"));
            AddSeparator();
        }
        else if (selected.Length > 0)
        {
            FilesMate.App.Localization.StringTable.UseUiCulture = true;
            var primary = selected[0];
            bool Archive(SearchRow row) => FilesMate.App.Icons.FileTypeIconCatalog.IsArchivePath(row.Path);
            var context = new CommandContext(CommandSurface.Menu, selected.Length, primary.Hit.IsDirectory, false, false,
                PrimaryPath: primary.Path, TagsAvailable: true, BatchRenameAvailable: true, ShareAvailable: true,
                FolderPath: Path.GetDirectoryName(primary.Path), PrimaryIsArchive: Archive(primary), SelectionIsArchive: selected.All(Archive));
            var layout = FileContextMenuBuilder.BuildLayout(context);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 0, 2, 2) };
            foreach (var command in layout.Primary)
            {
                var id = command.Id.ToString();
                var button = new Button { Content = Glyph(command.Glyph), Width = 36, Height = 36, Padding = new Thickness(0),
                    Margin = new Thickness(1, 0, 1, 0), IsEnabled = command.Enabled && (id != "Properties" || selected.Length == 1), ToolTip = command.Tooltip, Tag = id };
                AutomationProperties.SetName(button, command.Label);
                button.Click += (_, _) => Invoke(id);
                bar.Children.Add(button);
            }
            var toolbar = new MenuItem { Header = bar, StaysOpenOnClick = true, Focusable = false,
                Style = (Style)Resources["FileMenuToolbar"] };
            menu.Items.Add(toolbar);
            AddSeparator();
            if (selected.Length > 1)
            {
                menu.Items.Add(Item(Loc.Get("Command_Open"), "\uE8E5", "Open", "Enter"));
                menu.Items.Add(Item(Loc.Get("RevealFile"), "\uE8B7", "Reveal", "Ctrl+Enter"));
            }
            foreach (var entry in layout.Items)
            {
                if (entry.IsSeparator) { AddSeparator(); continue; }
                if (entry.IsShowMore) { menu.Items.Add(Item(entry.Label, entry.Glyph, "ShowMore")); continue; }
                if (entry.Command is not { } id) continue;
                if (id is AppCommandId.Compress or AppCommandId.Extract)
                {
                    var parent = new MenuItem { Header = entry.Label, Icon = Glyph(entry.Glyph), Tag = id.ToString() };
                    var children = id == AppCommandId.Compress
                        ? new[] { AppCommandId.CompressZip, AppCommandId.Compress7z, AppCommandId.CompressNew }
                        : new[] { AppCommandId.SmartExtract, AppCommandId.ExtractHere, AppCommandId.ExtractToFolder, AppCommandId.ExtractToOther };
                    foreach (var child in children)
                    {
                        var command = CommandCatalog.Resolve(child, context);
                        parent.Items.Add(Item(command.Label, command.Glyph, child.ToString(), enabled: command.Enabled));
                    }
                    menu.Items.Add(parent);
                }
                else if (id is AppCommandId.Open or AppCommandId.CopyPath || SearchFileAction.Supports(id.ToString()))
                {
                    menu.Items.Add(Item(entry.Label, entry.Glyph, id.ToString(), entry.Shortcut, entry.Enabled));
                    if (id == AppCommandId.Open)
                        menu.Items.Add(Item(Loc.Get("RevealFile"), "\uE8B7", "Reveal", "Ctrl+Enter"));
                }
            }
            AddSeparator();
        }
        if (selected.Length > 0)
        {
            menu.Items.Add(Item(Loc.Get("Search_HideResult"), "\uED1A", "Hide"));
            AddSeparator();
        }
        var settings = new MenuItem { Header = Loc.Get("Search_Settings"), Icon = Glyph("\uE713") };
        settings.Click += (_, _) => ShowSettings(true, "");
        menu.Items.Add(settings);
        menu.Opened += (_, _) => _contextOpen = true;
        menu.Closed += (_, _) =>
        {
            _contextOpen = false;
            _resultMenu = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_dragging && !IsActive && IsVisible) Dismiss(); }));
        };
        menu.IsOpen = true;
        if (keyboard) menu.Focus();
    }

    private static async Task ApplyArchiveProviderAsync(MenuItem item, string label, bool commandEnabled)
    {
        var availability = CompactMateSession.IsAvailableAsync();
        if (availability.IsCompletedSuccessfully)
        {
            Apply(availability.Result);
            return;
        }

        item.Header = label + " · " + Loc.Get("Archive_CheckingProvider");
        item.IsEnabled = false;
        bool found;
        try { found = await availability; }
        catch (Exception) { found = false; }
        if (item.Dispatcher.HasShutdownStarted || item.Dispatcher.HasShutdownFinished) return;
        Apply(found);

        void Apply(bool available)
        {
            item.Header = available ? label : label + " · " + Loc.Get("Archive_ExternalOnly");
            item.IsEnabled = commandEnabled && available;
            item.ToolTip = available ? null : Loc.Get("Archive_ExternalOnly");
            ToolTipService.SetShowOnDisabled(item, !available);
        }
    }

    private void InvokeFileCommand(string command)
    {
        if (_pending || SelectedRows() is not { Length: > 0 } rows) return;
        if (command == "Hide") { HideResults(rows); return; }
        if (command == "Recycle") { ConfirmRecycleResults(rows); return; }
        if (rows.Any(row => !Exists(row))) { ActionError(Loc.Get("Search_StaleFiles")); return; }
        switch (command)
        {
            case "Open": OpenSelected(false); return;
            case "Reveal": OpenSelected(true); return;
            case "Copy": CopyFiles(); return;
            case "Cut": CopyFiles(true); return;
            case "CopyPath": CopySelectedPath(); return;
            case "Properties": ShowProperties(); return;
        }
        if (rows.Any(row => row.IsApplication)) { ActionError(Loc.Get("Search_AppEntryHint")); return; }
        try
        {
            _host.RunFileAction(command, rows.Select(row => row.Path).ToArray());
            Dismiss();
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        { ActionError(Loc.Get("Action_FailedPrefix") + error.Message); }
    }
}
