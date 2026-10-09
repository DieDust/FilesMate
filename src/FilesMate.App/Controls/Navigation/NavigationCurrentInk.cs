using System.Runtime.CompilerServices;
using FilesMate.App.Theming;
using FilesMate.App.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FilesMate.App.Controls.Navigation;

/// <summary>Maps the sidebar's model selection to a live base ink without selecting its ListView.</summary>
public static class NavigationCurrentInk
{
    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.RegisterAttached(
        "IsCurrent", typeof(bool), typeof(NavigationCurrentInk), new PropertyMetadata(false, Changed));
    private static readonly ConditionalWeakTable<ListViewItem, References> Owners = new();

    public static bool GetIsCurrent(DependencyObject owner) => (bool)owner.GetValue(IsCurrentProperty);
    public static void SetIsCurrent(DependencyObject owner, bool value) => owner.SetValue(IsCurrentProperty, value);

    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is ListViewItem item) Refresh(item);
    }

    internal static void Refresh(ListViewItem item) => Owners.GetValue(item, owner => new References(owner)).Refresh();

    private sealed class References
    {
        private readonly ListViewItem _owner;
        private bool _refreshQueued;
        private bool _pointerOver;
        private bool _pointerPressed;
        private bool _keyboardPressed;

        public References(ListViewItem owner)
        {
            _owner = owner;
            owner.Loaded += (_, _) => Refresh();
            owner.IsEnabledChanged += (_, _) => { if (!owner.IsEnabled) ResetPress(); Refresh(); };
            owner.Unloaded += (_, _) => { _pointerOver = false; ResetPress(); };
            owner.PointerEntered += (_, _) => { _pointerOver = true; RefreshInk(); };
            owner.PointerExited += (_, _) => { _pointerOver = false; RefreshInk(); };
            // Native item class handlers may consume presses. Observe without changing input handling.
            owner.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { _pointerPressed = true; RefreshInk(); }), true);
            owner.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => { _pointerPressed = false; RefreshInk(); }), true);
            owner.PointerCanceled += (_, _) => { _pointerPressed = false; RefreshInk(); };
            owner.PointerCaptureLost += (_, _) => { _pointerPressed = false; RefreshInk(); };
            owner.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
            {
                if (args.Key is VirtualKey.Space or VirtualKey.Enter) { _keyboardPressed = true; RefreshInk(); }
            }), true);
            owner.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, args) =>
            {
                if (args.Key is VirtualKey.Space or VirtualKey.Enter) { _keyboardPressed = false; RefreshInk(); }
            }), true);
            owner.LostFocus += (_, _) => ResetPress();
        }

        public void Refresh()
        {
            RefreshInk();
            PaintSelectionBackground();
            // A recycled container may receive the new model before its template is realized.
            if (_refreshQueued) return;
            _refreshQueued = true;
            if (!_owner.DispatcherQueue.TryEnqueue(() =>
            {
                _refreshQueued = false;
                if (_owner.IsLoaded) PaintSelectionBackground();
            })) _refreshQueued = false;
        }

        private void ResetPress()
        {
            _pointerPressed = false;
            _keyboardPressed = false;
            RefreshInk();
        }

        private void RefreshInk() => ThemeResources.Bind(_owner, Control.ForegroundProperty,
            SidebarInkPolicy.Resource(_owner.IsEnabled, GetIsCurrent(_owner),
                _pointerOver || _pointerPressed || _keyboardPressed));

        private void PaintSelectionBackground()
        {
            if (!_owner.IsLoaded) return;
            Visit(_owner);
            void Visit(DependencyObject parent)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                {
                    var child = VisualTreeHelper.GetChild(parent, index);
                    if (child is Border { Name: "SelectionBackground" } selection)
                        ThemeResources.Bind(selection, Border.BackgroundProperty, _owner.IsEnabled
                            ? "FilesMate.Item.SelectedBrush" : "FilesMate.App.SolidBackgroundBrush");
                    else Visit(child);
                }
            }
        }
    }
}
