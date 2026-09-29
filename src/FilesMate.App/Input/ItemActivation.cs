using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace FilesMate.App.Input;

internal static class ItemActivation
{
    // Share between Home and all panes in a window so navigation cannot turn the
    // second half of a single-click-mode double-click into another open action.
    private static readonly ConditionalWeakTable<XamlRoot, ItemClickTracker> Trackers = new();
    internal static readonly InputSystemCursor HandCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    internal static void BindName(TextBlock name, Func<ItemOpeningMode> mode, Action<InputCursor?> cursor)
    {
        name.PointerEntered += (_, _) => Hover(true);
        name.PointerMoved += (_, _) => Hover(true);
        name.PointerExited += (_, _) => Hover(false);
        void Hover(bool over)
        {
            var current = mode();
            cursor(current == ItemOpeningMode.SingleClick || (over && current == ItemOpeningMode.NameClick) ? HandCursor : null);
            name.TextDecorations = over && current == ItemOpeningMode.NameClick
                ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None;
        }
    }

    internal static bool HasSelectionModifier => IsDown(VirtualKey.Control) || IsDown(VirtualKey.Shift)
        || IsDown(VirtualKey.Menu);

    internal static bool ShouldOpen(FrameworkElement element, string identity, Point position, ItemOpeningMode mode, bool onName)
    {
        if (element.XamlRoot is not { } root) return false;
        var scale = root.RasterizationScale;
        return Trackers.GetOrCreateValue(root).ShouldOpen(identity, mode, onName,
            Environment.TickCount64, position.X, position.Y, (int)GetDoubleClickTime(),
            GetSystemMetrics(36) / (2 * scale), GetSystemMetrics(37) / (2 * scale));
    }

    internal static void CancelPendingClick(FrameworkElement element)
    {
        if (element.XamlRoot is { } root && Trackers.TryGetValue(root, out var tracker))
            tracker.CancelPendingClick();
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
