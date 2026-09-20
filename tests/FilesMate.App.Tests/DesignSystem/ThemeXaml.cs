using System.Xml.Linq;

namespace FilesMate.App.Tests.DesignSystem;

internal static class ThemeXaml
{
    internal static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    internal static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static readonly string[] SemanticBrushKeys =
    [
        "FilesMate.App.BackgroundBrush",
        "FilesMate.AddressBar.BackgroundBrush",
        "FilesMate.AddressBar.BorderBrush",
        "FilesMate.TextControl.CaretHostBrush",
        "FilesMate.Toolbar.BackgroundBrush",
        "FilesMate.Sidebar.BackgroundBrush",
        "FilesMate.Shell.SeparatorBrush",
        "FilesMate.Favorites.BackgroundBrush",
        "FilesMate.FileContent.BackgroundBrush",
        "FilesMate.Sidebar.SeparatorBrush",
        "FilesMate.FileContent.BorderBrush",
        "FilesMate.FileArea.BackgroundBrush",
        "FilesMate.FileArea.InactiveBackgroundBrush",
        "FilesMate.InfoPane.BackgroundBrush",
        "FilesMate.Card.BorderBrush",
        "FilesMate.Item.HoverBrush",
        "FilesMate.Item.SelectedBrush",
        "FilesMate.Item.SelectedHoverBrush",
        "FilesMate.Item.DropTargetBrush",
        "FilesMate.Selection.AccentBrush",
        "FilesMate.Text.PrimaryBrush",
        "FilesMate.Text.SecondaryBrush",
        "FilesMate.Divider.Brush",
        "FilesMate.Glass.SurfaceBrush",
        "FilesMate.Glass.SurfaceHoverBrush",
        "FilesMate.Glass.SurfacePressedBrush",
        "FilesMate.Glass.BorderBrush",
        "FilesMate.Glass.BorderStrongBrush",
        "FilesMate.Glass.HighlightBrush",
        "FilesMate.Glass.CardBrush",
        "FilesMate.SettingsNav.BackgroundBrush",
        "FilesMate.SettingsCard.BackgroundBrush",
        "FilesMate.ComboBox.BackgroundBrush",
        "FilesMate.ComboBox.BorderBrush",
        "FilesMate.Glass.AccentBrush",
        "FilesMate.Glass.AccentHoverBrush",
        "FilesMate.Glass.AccentSoftBrush",
        "FilesMate.Glass.AccentForegroundBrush",
        "FilesMate.Glass.SheenBrush",
        "FilesMate.Chrome.FillBrush",
        "FilesMate.LiquidGlass.SolidFillBrush",
        "FilesMate.LiquidGlass.FillBrush",
        "FilesMate.LiquidGlass.BorderBrush",
        "FilesMate.LiquidGlass.InnerHighlightBrush",
        "FilesMate.LiquidGlass.ShadowBrush",
        "FilesMate.Menu.BackgroundBrush",
        "FilesMate.ThemePreview.LightFill",
        "FilesMate.ThemePreview.DarkFill",
    ];

    internal static readonly string[] ThemeNames = ["Light", "Dark", "HighContrast"];

    internal static readonly string[] MergeOrder =
    [
        "Themes/DesignTokens.xaml",
        "Themes/AppThemeResources.xaml",
        "Themes/LiquidGlassStyles.xaml",
        "Themes/ButtonStyles.xaml",
        "Themes/NavigationStyles.xaml",
        "Themes/TabStyles.xaml",
        "Themes/FileSurfaceStyles.xaml",
        "Themes/MenuStyles.xaml",
    ];

    internal static readonly string[] MigratedStyleKeys =
    [
        "QuietButtonStyle",
        "ToolbarIconButtonStyle",
        "CommandBarButtonStyle",
        "GlassButtonStyle",
        "PillButtonStyle",
        "SidebarItemStyle",
        "ColumnHeaderButtonStyle",
    ];

    internal static string RepoRoot { get; } = FindRepoRoot();

    internal static string AppRoot => Path.Combine(RepoRoot, "src", "FilesMate.App");

    internal static string ThemesRoot => Path.Combine(AppRoot, "Themes");

    internal static XDocument Load(string relativePath)
    {
        var path = Path.Combine(AppRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing theme dictionary: {path}");
        return XDocument.Load(path);
    }

    internal static IReadOnlyDictionary<string, XElement> ThemeDictionaries(XDocument document)
    {
        var root = document.Root ?? throw new InvalidOperationException("Resource dictionary has no root.");
        var host = root.Element(Presentation + "ResourceDictionary.ThemeDictionaries");
        Assert.NotNull(host);

        var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var dictionary in host.Elements(Presentation + "ResourceDictionary"))
        {
            var name = (string?)dictionary.Attribute(Xaml + "Key");
            Assert.False(string.IsNullOrWhiteSpace(name), "Theme dictionary is missing x:Key.");
            Assert.True(map.TryAdd(name, dictionary), $"Duplicate theme dictionary '{name}'.");
        }

        return map;
    }

    internal static HashSet<string> Keys(XElement dictionary)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in dictionary.Elements())
        {
            if (child.Name == Presentation + "ResourceDictionary.ThemeDictionaries")
            {
                continue;
            }

            var key = (string?)child.Attribute(Xaml + "Key");
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            Assert.True(keys.Add(key), $"Duplicate resource key '{key}' in {dictionary.Name.LocalName}.");
        }

        return keys;
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FilesMate.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
