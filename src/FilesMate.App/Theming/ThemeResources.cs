using System.Runtime.CompilerServices;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Theming;

/// <summary>
/// Live theme references for elements built in C#. XAML uses ThemeResource directly.
/// Resolve against the element's theme instead of Application.RequestedTheme, which is frozen at startup.
/// </summary>
internal static class ThemeResources
{
    private static readonly ConditionalWeakTable<FrameworkElement, References> Bindings = new();

    public static void Bind(FrameworkElement element, DependencyProperty property, string key)
    {
        Bindings.GetValue(element, owner => new References(owner)).Set(property, key);
    }

    public static void Clear(FrameworkElement element, DependencyProperty property)
    {
        if (Bindings.TryGetValue(element, out var references)) references.Remove(property);
        element.ClearValue(property);
    }

    public static object? Resolve(FrameworkElement? element, string key)
    {
        var theme = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? "HighContrast" : ContentDialogTheme.Resolve(element).ToString();
        return Find(Application.Current.Resources, theme, key)
            ?? (Application.Current.Resources.TryGetValue(key, out var fallback) ? fallback : null);
    }

    private static object? Find(ResourceDictionary resources, string theme, string key)
    {
        if (resources.ThemeDictionaries.TryGetValue(theme, out var value) && value is ResourceDictionary palette
            && palette.TryGetValue(key, out var themed)) return themed;
        for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
            if (Find(resources.MergedDictionaries[i], theme, key) is { } found) return found;
        return null;
    }

    private sealed class References
    {
        private readonly FrameworkElement _owner;
        private readonly Dictionary<DependencyProperty, string> _keys = [];
        private bool _subscribed;

        public References(FrameworkElement owner)
        {
            _owner = owner;
            owner.Loaded += (_, _) => { Subscribe(); Refresh(); };
            owner.Unloaded += (_, _) => Unsubscribe();
            owner.ActualThemeChanged += (_, _) => Refresh();
            if (owner.IsLoaded) Subscribe();
        }

        public void Set(DependencyProperty property, string key)
        {
            _keys[property] = key;
            if (Resolve(_owner, key) is { } value) _owner.SetValue(property, value);
        }

        public void Remove(DependencyProperty property) => _keys.Remove(property);

        private void Subscribe()
        {
            if (_subscribed) return;
            App.AppearanceChanged += Changed;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            App.AppearanceChanged -= Changed;
            _subscribed = false;
        }

        private void Changed(object? sender, AppearanceSettings settings) => Refresh();
        private void Refresh()
        {
            foreach (var (property, key) in _keys)
                if (Resolve(_owner, key) is { } value) _owner.SetValue(property, value);
        }
    }
}
