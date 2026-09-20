using FilesMate.App.Localization;
using FilesMate.App.Shortcuts;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.System;

namespace FilesMate.App.Controls.Settings;

public sealed partial class ShortcutEditorRow : UserControl
{
    private bool _capturing;
    private ShortcutGesture _gesture;

    public ShortcutEditorRow()
    {
        InitializeComponent();
        Unloaded += (_, _) => { if (_capturing) CancelCapture(); };
    }

    public ShortcutAction Action { get; private set; }

    public event EventHandler<ShortcutGesture>? GestureSubmitted;

    public void Configure(
        ShortcutAction action,
        string title,
        ShortcutGesture gesture,
        bool showDivider)
    {
        Action = action;
        TitleText.Text = title;
        ToolTipService.SetToolTip(GestureButton, StringTable.Get("Shortcut_Lead"));
        Divider.Visibility = showDivider ? Visibility.Visible : Visibility.Collapsed;
        SetGesture(gesture);
    }

    public void SetGesture(ShortcutGesture gesture)
    {
        _gesture = gesture;
        if (!_capturing)
        {
            GestureButton.Content = gesture.DisplayText;
        }

        ClearError();
    }

    public void ShowConflict(string actionName)
    {
        ErrorText.Text = StringTable.Format("Shortcut_Conflict", actionName);
        ErrorText.Visibility = Visibility.Visible;
    }

    private void GestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_capturing)
        {
            return;
        }

        _capturing = true;
        App.IsShortcutCaptureActive = true;
        ClearError();
        GestureButton.Content = StringTable.Get("Shortcut_PressKeys");
        GestureButton.Focus(FocusState.Programmatic);
    }

    private void GestureButton_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            CancelCapture();
            return;
        }

        var key = (ShortcutKey)(int)e.Key;
        if (key is ShortcutKey.Control or ShortcutKey.Menu or ShortcutKey.Shift
            or ShortcutKey.LeftWindows or ShortcutKey.RightWindows)
        {
            return;
        }

        var gesture = new ShortcutGesture(key, ReadModifiers());
        if (!gesture.IsValid)
        {
            return;
        }

        _capturing = false;
        App.IsShortcutCaptureActive = false;
        GestureButton.Content = gesture.DisplayText;
        GestureSubmitted?.Invoke(this, gesture);
    }

    private void GestureButton_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturing)
        {
            CancelCapture();
        }
    }

    private void CancelCapture()
    {
        _capturing = false;
        App.IsShortcutCaptureActive = false;
        GestureButton.Content = _gesture.DisplayText;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private static ShortcutModifiers ReadModifiers()
    {
        var modifiers = ShortcutModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= ShortcutModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= ShortcutModifiers.Menu;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= ShortcutModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= ShortcutModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
