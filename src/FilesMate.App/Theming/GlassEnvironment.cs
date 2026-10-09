using Windows.UI.ViewManagement;

namespace FilesMate.App.Theming;

/// <summary>Read-only system gates shared by connected and floating WinUI surfaces.</summary>
internal static class GlassEnvironment
{
    private static readonly Lazy<AccessibilitySettings> Accessibility = new(() => new AccessibilitySettings());
    private static readonly Lazy<UISettings> Effects = new(() => new UISettings());

    public static bool HighContrast
    {
        get { try { return Accessibility.Value.HighContrast; } catch { return true; } }
    }

    internal static bool TransparencyEnabled
    {
        get
        {
            try { return !HighContrast && Effects.Value.AdvancedEffectsEnabled; }
            catch { return false; }
        }
    }
}
