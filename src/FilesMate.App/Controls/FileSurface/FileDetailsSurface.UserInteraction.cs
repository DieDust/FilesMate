using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.System;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    // An ownership epoch, not a count of gestures: routed input and its
    // explicit command may both claim the same user interaction.
    internal long UserInteractionVersion { get; private set; }

    internal void NotifyUserInteraction()
    {
        CancelNameClickRename(retainPrevious: true);
        DeferListNameMeasurementForInput();
        unchecked { ++UserInteractionVersion; }
        ExtendRecursiveInputQuietPeriod();
        CancelScrollRestore();
    }

    private void InitializeUserInteractionTracking()
    {
        // Observe handled pointer input without running selection behavior for
        // a TextBox, checkbox, scrollbar, or alphabet control. Preview keys are
        // observed before those children handle them.
        // Register before InitializeComponent wires the XAML behavior handlers:
        // Back/Up can create a new restore synchronously during those handlers.
        AddHandler(PointerPressedEvent, new PointerEventHandler(ObserveUserPointerInput), true);
        // The click tracker starts its interval at release. Extend only the
        // recursive quiet timer here, without changing navigation ownership.
        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => ExtendRecursiveInputQuietPeriod()), true);
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(ObserveUserKeyInput), true);
    }

    private void ExtendRecursiveInputQuietPeriod()
    {
        if (!_recursiveMode) return;
        _recursiveLastInput = System.Diagnostics.Stopwatch.GetTimestamp();
        _recursiveInputQuietPeriod = FilesMate.App.Input.ItemActivation.DoubleClickInterval;
    }

    private void ObserveUserPointerInput(object sender, PointerRoutedEventArgs e)
    {
        // These buttons navigate away rather than take ownership of this view.
        // A child/accelerator may already have started the destination restore.
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind is
            PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed) return;
        NotifyUserInteraction();
    }

    private void ObserveUserKeyInput(object sender, KeyRoutedEventArgs e)
    {
        CancelNameClickRename();
        // Window preview handlers may already have refreshed or navigated and
        // created the destination's restore. Their selection/rename commands
        // claim ownership explicitly; do not reinterpret handled window input.
        if (e.Handled) return;
        if (IsModifier(VirtualKey.Menu) && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up) return;
        if (e.Key == VirtualKey.Back && !IsModifier(VirtualKey.Control) && !IsModifier(VirtualKey.Shift)
            && !IsModifier(VirtualKey.Menu) && !IsRenaming
            && FocusManager.GetFocusedElement(XamlRoot) is not TextBox) return;
        NotifyUserInteraction();
    }
}
