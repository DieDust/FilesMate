using FilesMate.App.Models;
using FilesMate.App.Themes;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Theming;

// An independent instance of the product palette. The production publisher
// updates its existing brushes, so native templates retain live references.
internal sealed class LocalSkinScope
{
    private readonly FrameworkElement _host;
    private readonly ResourceDictionary _resources = new() { Source = new Uri("ms-appx:///Themes/AppThemeResources.xaml") };
    internal LocalSkinScope(FrameworkElement host)
    {
        _host = host;
        host.Resources.MergedDictionaries.Add(_resources);
        ThemeResources.RegisterLocalPalette(host, _resources);
    }
    internal void Apply(AppearanceSettings settings)
    {
        ShellStyleResources.Apply(_resources, settings);
        _host.RequestedTheme = settings.Theme switch
        {
            AppThemeKind.Light => ElementTheme.Light,
            AppThemeKind.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
