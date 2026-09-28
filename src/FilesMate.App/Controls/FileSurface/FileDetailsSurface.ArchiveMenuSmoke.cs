#if FILESMATE_UI_TEST
using FilesMate.App.Localization;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    internal async Task InvokeArchiveMenuSmokeAsync(IReadOnlyList<string> paths)
    {
        var store = new EntryStore();
        store.Append(paths.Select((path, index) => new FileEntryCore(index + 1,
            Path.GetFileName(path), 0, DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks,
            FileAttributes.Normal, EntryKind.File)).ToArray());
        ResolvePath = entry => paths[entry.Id - 1];
        ResolveFolder = () => Path.GetDirectoryName(paths[0]);
        Bind(store, EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, StringComparer.Ordinal, 1), 1);
        _selection.SelectAll(_items.Store!, _items.Index!);
        RefreshRealizedSelection();
        ShowContextMenu(false, new Point(300, 150));
        await Task.Delay(300);
        var extract = PopupElements().OfType<Button>().FirstOrDefault(button =>
            Descendants(button).OfType<TextBlock>().Any(text => text.Text == StringTable.Get("Command_Extract")))
            ?? throw new IOException("Decorated selection has no Extract menu.");
        if (!extract.IsEnabled) throw new IOException("Extract menu is disabled.");
        ((IInvokeProvider)new ButtonAutomationPeer(extract).GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(300);
        var here = PopupElements().OfType<MenuFlyoutItem>()
            .FirstOrDefault(item => item.Text == StringTable.Get("Command_ExtractHere"))
            ?? throw new IOException("ExtractHere submenu is missing.");
        if (!here.IsEnabled) throw new IOException("ExtractHere is disabled.");
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(here).GetPattern(PatternInterface.Invoke)).Invoke();

        IEnumerable<DependencyObject> PopupElements() => VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
            .SelectMany(popup => Descendants(popup.Child).Prepend(popup.Child));
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
#endif
