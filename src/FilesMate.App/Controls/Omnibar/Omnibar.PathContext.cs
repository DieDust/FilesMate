using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar
{
    private string _pathEditBase = "";
    private readonly AddressInputLifetime _pathInputLifetime = new();
    private (long Revision, string Draft, string Base)? _pasteFailureRestore;
    private long? _pasteSubmissionRevision;
    public long AddressInputRevision => _pathInputLifetime.Revision;
    public void InvalidateAddressInput() => _pathInputLifetime.Changed();
    public void CancelPendingPathPaste() => _pathInputLifetime.CancelClipboardReads();
    public bool IsPastePathSubmission => _pasteSubmissionRevision == AddressInputRevision;
    private bool _pathContextOpen;
    private int _pathContextVersion;
    private string DisplayDirectory => _selectionDisplayIsFile && _selectionDisplayPath is not null
        ? System.IO.Path.GetDirectoryName(_selectionDisplayPath) ?? _session.Path : DisplayPath;
    public string AddressInputBasePath => IsEditing ? _pathEditBase : DisplayDirectory;
    internal Func<Task<string?>> ReadPathClipboardAsync { get; set; } = ReadSystemPathClipboardAsync;
    internal Action<string> WritePathClipboardText { get; set; } = WriteSystemPathClipboard;

    private void InitializePathContextMenu()
    {
        // TextChanging is synchronous. A delayed TextChanged notification from
        // our own assignment must not invalidate the submission it just started.
        PathBox.TextChanging += (_, _) => _pathInputLifetime.Changed();
        var isolatedClipboard = System.IO.Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "diagnostics", "address-clipboard-fixture.json");
        if (FilesMate.Core.Configuration.RuntimeProfile.IsIsolated && System.IO.File.Exists(isolatedClipboard))
        {
            // Explicit fixture injection in an isolated profile. Native clipboard
            // is neither replaced nor restored by this test service.
            ReadPathClipboardAsync = async () =>
            {
                var json = await System.IO.File.ReadAllTextAsync(isolatedClipboard);
                if (json.Length > 32768) return null;
                var fixture = System.Text.Json.JsonSerializer.Deserialize<PathClipboardFixture>(json);
                return fixture?.IsText == true ? fixture.Text : null;
            };
            WritePathClipboardText = text => System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(isolatedClipboard)!, "address-copy-fixture-result.json"), System.Text.Json.JsonSerializer.Serialize(new { CopiedText = text, NativeClipboardChanged = false }));
        }
        PathViewport.RightTapped += PathDisplay_RightTapped;
        var editMenu = new TextCommandBarFlyout { AlwaysExpanded = true };
        var paste = new AppBarButton { Label = StringTable.Get("Address_PasteGo"), Icon = new FontIcon { Glyph = "\uE77F" }, IsEnabled = false };
        AutomationProperties.SetAutomationId(paste, "AddressEditPasteGo");
        editMenu.SecondaryCommands.Add(new AppBarSeparator()); editMenu.SecondaryCommands.Add(paste);
        editMenu.Opened += (_, _) =>
        {
            // TextCommandBarFlyout rebuilds its native text commands when it
            // opens. Append the address command after that population step.
            if (!editMenu.SecondaryCommands.Contains(paste))
            {
                editMenu.SecondaryCommands.Add(new AppBarSeparator());
                editMenu.SecondaryCommands.Add(paste);
            }
        };
        editMenu.Opening += async (_, _) =>
        {
            _pathContextOpen = true; var version = ++_pathContextVersion; var basis = AddressInputBasePath; var inputRevision = AddressInputRevision;
            paste.IsEnabled = false;
            var text = await ReadPathClipboardSafelyAsync();
            if (version == _pathContextVersion && _pathContextOpen && inputRevision == AddressInputRevision) paste.IsEnabled = AddressPath.TryNormalizeForPaste(text, basis, out _);
        };
        editMenu.Closed += (_, _) => { _pathContextOpen = false; ++_pathContextVersion; };
        paste.Click += async (_, _) => await PastePathAndGoAsync(AddressInputBasePath);
        PathBox.ContextFlyout = editMenu;
    }

    private void ConfigurePathContext(FrameworkElement element, string path, bool isFile)
    {
        element.RightTapped += (_, e) =>
        {
            if (IsEditing) return;
            e.Handled = true;
            ShowPathDisplayMenu(element, e.GetPosition(element), path, isFile);
        };
    }

    private void PathDisplay_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (IsEditing || e.Handled) return;
        e.Handled = true;
        ShowPathDisplayMenu(PathViewport, e.GetPosition(PathViewport), DisplayPath, _selectionDisplayIsFile);
    }

    private void ShowPathDisplayMenu(FrameworkElement target, Windows.Foundation.Point point, string displayed, bool isFile)
    {
        var inputRevision = AddressInputRevision;
        var basis = isFile ? System.IO.Path.GetDirectoryName(displayed) ?? _session.Path : displayed;
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["FilesMate.MenuFlyoutPresenterStyle"] };
        MenuFlyoutItem Item(string key, string glyph, string id)
        {
            var item = new MenuFlyoutItem { Text = StringTable.Get(key), Icon = new FontIcon { Glyph = glyph } };
            AutomationProperties.SetAutomationId(item, id); menu.Items.Add(item); return item;
        }
        var copy = Item("Command_CopyPath", "\uE71B", "AddressCopyPath");
        copy.Click += (_, _) => WritePathClipboardText(displayed);
        var edit = Item("Address_EditPath", "\uE8AC", "AddressEditPath");
        edit.Click += (_, _) => BeginPathEdit();
        menu.Items.Add(new MenuFlyoutSeparator());
        var paste = Item("Address_PasteGo", "\uE77F", "AddressPasteGo"); paste.IsEnabled = false;
        paste.Click += async (_, _) => { if (inputRevision == AddressInputRevision) await PastePathAndGoAsync(basis); };
        menu.Opening += async (_, _) =>
        {
            _pathContextOpen = true; var version = ++_pathContextVersion;
            var text = await ReadPathClipboardSafelyAsync();
            if (version == _pathContextVersion && _pathContextOpen && inputRevision == AddressInputRevision) paste.IsEnabled = AddressPath.TryNormalizeForPaste(text, basis, out _);
        };
        menu.Closed += (_, _) => { _pathContextOpen = false; ++_pathContextVersion; };
        FlyoutTheme.FollowHost(menu);
        FilesMate.App.Controls.Menus.FileContextPopupPlacement.ShowAt(menu, target, point);
    }

    private async Task PastePathAndGoAsync(string basis)
    {
        var read = _pathInputLifetime.BeginClipboardRead();
        var previousDraft = IsEditing ? PathBox.Text : null;
        var previousBase = _pathEditBase;
        var text = await ReadPathClipboardSafelyAsync();
        if (!_pathInputLifetime.IsCurrent(read) || !IsLoaded
            || !AddressPath.TryNormalizeForPaste(text, basis, out _)) return;
        BeginPathEdit();
        _pathEditBase = basis;
        // Preserve the user's exact input, including quotes, in a failed display
        // submission. In an existing editor, restore its prior draft on failure.
        PathBox.Text = text!;
        _session.SetDraft(text!);
        if (previousDraft is not null) _pasteFailureRestore = (AddressInputRevision, previousDraft, previousBase);
        _pasteSubmissionRevision = AddressInputRevision;
        PathSubmitted?.Invoke(this, text!);
    }

    private async Task<string?> ReadPathClipboardSafelyAsync()
    {
        try { return await ReadPathClipboardAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception) { return null; } // Clipboard contents/errors are never logged.
    }

    private static async Task<string?> ReadSystemPathClipboardAsync()
    {
        var data = Clipboard.GetContent();
        return data.Contains(StandardDataFormats.Text) ? await data.GetTextAsync() : null;
    }

    private sealed record PathClipboardFixture(bool IsText, string? Text);
    private static void WriteSystemPathClipboard(string text)
    {
        var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data);
    }
}
