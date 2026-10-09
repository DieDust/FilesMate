using FilesMate.App.Shortcuts;
using FilesMate.App.Views;
using FilesMate.Core.Diagnostics;
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
        var previewOperation = args.Key == VirtualKey.Space ? PerformanceTrace.NextOperation() : 0;
        if (args.Key == VirtualKey.Space) PerformanceTrace.Record(PerformanceStage.PreviewKeyReceived,
            operation: previewOperation, count: args.Handled ? 1 : 0);
        if (args.Handled || FileShortcutRoutingBlocked
            || (IsTextInputElement(args.OriginalSource as DependencyObject)
                && (args.Key == VirtualKey.V && Down(VirtualKey.Control)
                    || args.Key == VirtualKey.Insert && Down(VirtualKey.Shift))))
        {
            if (args.Key == VirtualKey.Space) PerformanceTrace.Record(PerformanceStage.PreviewBlocked,
                operation: previewOperation, outcome: PerformanceOutcome.Cancelled, auxiliary: 1);
            return;
        }
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
        // A focused preview toolbar Button consumes Space before the file surface's
        // ordinary KeyDown. Route default Space in the preview phase while keeping
        // text editors, other toolbar buttons and configured bindings unchanged.
        if (args.Key == VirtualKey.Space && modifiers == VirtualKeyModifiers.None && !TextInputFocused && SpacePreviewFocus())
        {
            PerformanceTrace.Record(PerformanceStage.PreviewCommand, operation: previewOperation);
            args.Handled = TabHost.Content switch
            {
                NavigatorPage page => page.InvokeWindowQuickPreview(previewOperation),
                SearchResultsPage search => search.InvokeWindowQuickPreview(previewOperation),
                _ => false,
            };
            if (args.Handled) return;
            PerformanceTrace.Record(PerformanceStage.PreviewBlocked, operation: previewOperation,
                outcome: PerformanceOutcome.Cancelled, auxiliary: 2);
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

    private bool SpacePreviewFocus()
    {
        if (((FrameworkElement)Content).XamlRoot is not { } root) return false;
        for (var node = FocusManager.GetFocusedElement(root) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FilesMate.App.Controls.FileSurface.FileDetailsSurface) return true;
            if (node is Button { Name: "PreviewButton" }) return true;
            if (node is TextBox or RichEditBox or PasswordBox or AutoSuggestBox || node is ComboBox { IsEditable: true }) return false;
        }
        return false;
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
        || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is not ToolTip);

    internal bool TextInputFocused
    {
        get
        {
            var root = ((FrameworkElement)Content).XamlRoot;
            if (root is null) return false;
            return IsTextInputElement(FocusManager.GetFocusedElement(root) as DependencyObject);
        }
    }

    private static bool IsTextInputElement(DependencyObject? node)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is TextBox or RichEditBox or PasswordBox or AutoSuggestBox || node is ComboBox { IsEditable: true }) return true;
        return false;
    }

    private bool ClearSelectionFromWindow() => TabHost.Content switch
    {
        NavigatorPage page => page.HandleEscapeFromWindow(),
        SearchResultsPage search => search.HandleEscapeFromWindow(),
        _ => false
    };
}
