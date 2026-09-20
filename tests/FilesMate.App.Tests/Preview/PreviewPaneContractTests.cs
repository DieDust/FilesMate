using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Preview;

public sealed class PreviewPaneContractTests
{
    [Fact]
    public void Navigator_exposes_a_hidden_preview_pane_and_alt_p_shortcut()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        Assert.Contains("PreviewContainer", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewAccelerator_Invoked", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"P\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewClicked", xaml, StringComparison.Ordinal);

        var toolbar = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Toolbar", "AdaptiveCommandToolbar.xaml"));
        Assert.Contains("x:Name=\"PreviewButton\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE946;\"", toolbar, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_pane_cancels_and_clears_heavy_content_when_hidden()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Preview", "PreviewPane.xaml.cs"));
        Assert.Contains("_loadCts?.Cancel", code, StringComparison.Ordinal);
        Assert.Contains("MediaContent.Source = null", code, StringComparison.Ordinal);
        Assert.Contains("ImageContent.Source = null", code, StringComparison.Ordinal);
        Assert.Contains("ReleasePdfPreview", code, StringComparison.Ordinal);
        Assert.Contains("view.Close()", code, StringComparison.Ordinal);
        Assert.Contains("PreviewTruncated", code, StringComparison.Ordinal);
        Assert.Contains("ItemHeader", code, StringComparison.Ordinal);
        Assert.Contains("BindProperties", code, StringComparison.Ordinal);
        Assert.Contains("PreviewDetails.DisplayName", code, StringComparison.Ordinal);
        Assert.Contains("SetSourceAsync", code, StringComparison.Ordinal);
        Assert.Contains("CreateFromStorageFile", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new BitmapImage(new Uri", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_preview_creates_webview_lazily_and_falls_back_without_blocking_the_pane()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Preview", "PreviewPane.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Preview", "PreviewPane.xaml.cs"));

        Assert.Contains("ContentHost", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemName\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PropertyList\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<WebView2", xaml, StringComparison.Ordinal);
        Assert.Contains("new WebView2", code, StringComparison.Ordinal);
        Assert.Contains("EnsureCoreWebView2Async", code, StringComparison.Ordinal);
        Assert.Contains("PreviewPdfHint", code, StringComparison.Ordinal);
    }
}
