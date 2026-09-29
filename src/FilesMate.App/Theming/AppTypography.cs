using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Theming;

internal static class AppTypography
{
    internal static FontFamily Family { get; private set; } = new("XamlAutoFontFamily");
    private static string? _configured;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> Tracked = new();
    internal static void Configure(AppearanceSettings settings)
    {
        var name = settings.FileFontFamily ?? "XamlAutoFontFamily";
        if (_configured == name) return;
        _configured = name;
        Family = new FontFamily(name);
        Application.Current.Resources["ContentControlThemeFontFamily"] = Family;
        Application.Current.Resources["FilesMate.UiFontFamily"] = Family;
    }

    internal static void Track(FrameworkElement root)
    {
        if (!Tracked.TryGetValue(root, out _))
        {
            Tracked.Add(root, new object());
            root.Loaded += (_, _) => Refresh();
        }
        Refresh();
        void Refresh()
        {
            Apply(root);
            root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (root.IsLoaded) Apply(root); });
        }
    }

    internal static void Apply(DependencyObject? root)
    {
        if (root is null) return;
        if (root is FrameworkElement { Tag: "FilesMate.ContentTypography" }) return;
        switch (root)
        {
            case TextBlock text when IsSymbol(text.Text) && !Preserve(text.FontFamily):
                text.FontFamily = new FontFamily("Segoe Fluent Icons"); break;
            case FontIcon icon when !Preserve(icon.FontFamily):
                icon.FontFamily = new FontFamily("Segoe Fluent Icons"); break;
            case TextBlock text when !Preserve(text.FontFamily): text.FontFamily = Family; break;
            case RichTextBlock rich when !Preserve(rich.FontFamily): rich.FontFamily = Family; break;
            case Control control when !Preserve(control.FontFamily): control.FontFamily = Family; break;
            case ContentPresenter presenter when !Preserve(presenter.FontFamily): presenter.FontFamily = Family; break;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Apply(VisualTreeHelper.GetChild(root, i));
    }

    // Symbol fonts encode icons; document/code previews retain their content typography.
    private static bool IsSymbol(string text) => !string.IsNullOrEmpty(text) && text.All(c => char.IsWhiteSpace(c) || c is >= '\uE000' and <= '\uF8FF');
    private static bool Preserve(FontFamily font) => font.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase)
        || font.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase);
}
