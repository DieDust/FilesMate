using FilesMate.App.Shortcuts;
using FilesMate.App.Views;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<ShortcutAction, KeyboardAccelerator> _fileShortcuts = [];
    private static readonly (VirtualKey Key, VirtualKeyModifiers Modifiers)[] NavigationShortcuts =
    [
        (VirtualKey.Back, VirtualKeyModifiers.None),
        (VirtualKey.Left, VirtualKeyModifiers.Menu),
        (VirtualKey.Right, VirtualKeyModifiers.Menu),
        (VirtualKey.Up, VirtualKeyModifiers.Menu),
        (VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
    ];

    private void InitializeFileShortcuts()
    {
        var root = (UIElement)Content;
        // The tab strip and sidebar are siblings of the page. Fixed navigation
        // bindings need the same window scope as the configurable file actions.
        // Preview runs before controls such as TabView consume Alt+arrow keys.
        root.PreviewKeyDown += FileShortcut_PreviewKeyDown;
        foreach (var (key, modifiers) in NavigationShortcuts)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (sender, args) =>
            {
                if (FileShortcutRoutingBlocked) return;
                args.Handled = InvokeActiveNavigationShortcut(sender.Key);
            };
            root.KeyboardAccelerators.Add(accelerator);
        }
        foreach (var action in Enum.GetValues<ShortcutAction>().Where(action =>
            action is not (ShortcutAction.NewTab or ShortcutAction.CloseTab or ShortcutAction.ReopenTab)))
        {
            var accelerator = new KeyboardAccelerator();
            accelerator.Invoked += (sender, args) =>
            {
                if (FileShortcutRoutingBlocked) return;
                args.Handled = InvokeActiveFileShortcut(action);
            };
            _fileShortcuts.Add(action, accelerator);
            root.KeyboardAccelerators.Add(accelerator);
        }
        var selectAll = new KeyboardAccelerator { Key = VirtualKey.A, Modifiers = VirtualKeyModifiers.Control };
        selectAll.Invoked += (_, args) =>
        {
            if (FileShortcutRoutingBlocked || TextInputFocused) return;
            if (TabHost.Content is NavigatorPage page) args.Handled = page.SelectAllFromWindow();
            else if (TabHost.Content is SearchResultsPage search) args.Handled = search.SelectAllFromWindow();
        };
        root.KeyboardAccelerators.Add(selectAll);
    }

    private void FileShortcut_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || FileShortcutRoutingBlocked) return;
        var modifiers = VirtualKeyModifiers.None;
        if (Down(VirtualKey.Control)) modifiers |= VirtualKeyModifiers.Control;
        if (Down(VirtualKey.Menu)) modifiers |= VirtualKeyModifiers.Menu;
        if (Down(VirtualKey.Shift)) modifiers |= VirtualKeyModifiers.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= VirtualKeyModifiers.Windows;
        foreach (var (action, accelerator) in _fileShortcuts)
        {
            if (accelerator.Key != args.Key || accelerator.Modifiers != modifiers) continue;
            args.Handled = InvokeActiveFileShortcut(action);
            return;
        }
        foreach (var (key, required) in NavigationShortcuts)
        {
            if (args.Key != key || modifiers != required) continue;
            args.Handled = InvokeActiveNavigationShortcut(key);
            return;
        }
        if (args.Key == VirtualKey.A && modifiers == VirtualKeyModifiers.Control && !TextInputFocused)
            args.Handled = TabHost.Content switch
            {
                NavigatorPage page => page.SelectAllFromWindow(),
                SearchResultsPage search => search.SelectAllFromWindow(),
                _ => false
            };

        static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    }

    private bool InvokeActiveFileShortcut(ShortcutAction action) => TabHost.Content switch
    {
        NavigatorPage page => page.InvokeWindowShortcut(action),
        SearchResultsPage search => search.InvokeWindowShortcut(action),
        _ => false
    };

    private bool InvokeActiveNavigationShortcut(VirtualKey key) => TabHost.Content switch
    {
        NavigatorPage page => page.InvokeWindowNavigationShortcut(key),
        SearchResultsPage search => search.InvokeWindowNavigationShortcut(key),
        _ => false
    };

    internal bool FileShortcutRoutingBlocked => _windowClosed || App.IsShortcutCaptureActive
        || SettingsOverlay.Visibility == Visibility.Visible
        || ((FrameworkElement)Content).XamlRoot is not { } root
        || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0;

    internal bool TextInputFocused
    {
        get
        {
            var root = ((FrameworkElement)Content).XamlRoot;
            if (root is null) return false;
            for (var node = FocusManager.GetFocusedElement(root) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is TextBox or RichEditBox or PasswordBox or AutoSuggestBox || node is ComboBox { IsEditable: true }) return true;
            return false;
        }
    }

    private bool ClearSelectionFromWindow() => TabHost.Content switch
    {
        NavigatorPage page => page.HandleEscapeFromWindow(),
        SearchResultsPage search => search.HandleEscapeFromWindow(),
        _ => false
    };
}
