using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FilesMate.App.Controls.Navigation;

public sealed partial class WindowNavigation : UserControl
{
    private bool _userToggled, _resizing;
    private XamlRoot? _root;
    public NavigationSidebar Sidebar => Navigation;
    public bool IsOpen { get; private set; }
    public event EventHandler? StateChanged;

    public WindowNavigation()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _root = XamlRoot;
            if (_root is not null) _root.Changed += Root_Changed;
            App.FeaturesChanged += Settings_Changed;
            App.ExplorerPreferencesChanged += Preferences_Changed;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (_root is not null) _root.Changed -= Root_Changed;
            App.FeaturesChanged -= Settings_Changed;
            App.ExplorerPreferencesChanged -= Preferences_Changed;
        };
        Refresh();
    }
    public void Toggle()
    {
        _userToggled = true;
        App.SetSidebarCollapsed(IsOpen);
        Refresh();
    }
    private void Preferences_Changed(object? sender, ExplorerPreferences preferences) => DispatcherQueue.TryEnqueue(Refresh);
    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs e) => Refresh();
    private void Settings_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);
    private void Refresh()
    {
        var windowWidth = XamlRoot?.Size.Width ?? 0;
        IsOpen = !App.Features.SidebarCollapsed && (windowWidth < 1 || windowWidth >= 720 || _userToggled);
        Width = IsOpen ? ExplorerPreferences.ClampSidebarWidth(App.ExplorerPreferences.SidebarWidth) : 0;
        Navigation.IsCompact = ExplorerPreferences.SidebarIsCompact(Width);
        IsHitTestVisible = IsOpen;
        // Keep the sidebar mounted, including its scroll and section state.
        Navigation.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Resize_Pressed(object sender, PointerRoutedEventArgs e)
    { _resizing = ResizeThumb.CapturePointer(e.Pointer); e.Handled = _resizing; }
    private void Resize_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        Width = ExplorerPreferences.ClampSidebarWidth(e.GetCurrentPoint(this).Position.X);
        Navigation.IsCompact = ExplorerPreferences.SidebarIsCompact(Width);
        e.Handled = true;
    }
    private void Resize_Released(object sender, PointerRoutedEventArgs e)
    { if (!_resizing) return; SaveWidth(); ResizeThumb.ReleasePointerCapture(e.Pointer); e.Handled = true; }
    private void Resize_Lost(object sender, PointerRoutedEventArgs e) { if (_resizing) SaveWidth(); }
    private void SaveWidth()
    { _resizing = false; _ = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { SidebarWidth = Width }); }
}
