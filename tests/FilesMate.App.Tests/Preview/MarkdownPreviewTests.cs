using FilesMate.App.Preview;

namespace FilesMate.App.Tests.Preview;

public sealed class MarkdownPreviewTests
{
    [Fact]
    public void Reading_layout_renders_structure_and_preserves_code()
    {
        var html = MarkdownPreview.Render("# Heading\n\n**bold** and *italic*\n\n- item\n\n|A|B|\n|-|-|\n|1|2|\n\n```csharp\nvar x = 1;\n```", true, "#282828", "#FFFFFF", "#60CDFF");
        Assert.Contains("<h1>Heading</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<li>item</li>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<pre><code class=\"language-csharp\">var x = 1;", html);
        Assert.True(MarkdownPreview.CanHandle("README.MARKDOWN"));
    }

    [Fact]
    public void Documents_cannot_insert_active_html_and_follow_supplied_theme()
    {
        var html = MarkdownPreview.Render("<script>alert(1)</script>\n\n![picture](https://example.com/image.png)", false, "#FAFAFA", "#202020", "#0067C0");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("default-src 'none'", html);
        Assert.Contains("img-src https://filesmate-preview.local;", html);
        Assert.Contains("color-scheme: light", html);
        Assert.Contains("background:#FAFAFA", html);
        Assert.True(MarkdownPreview.IsDocumentNavigation("data:text/html;charset=utf-8;base64,PGgxPg==", false));
        Assert.False(MarkdownPreview.IsDocumentNavigation("data:text/html;charset=utf-8;base64,PGgxPg==", true));
        Assert.False(MarkdownPreview.IsDocumentNavigation("file:///C:/private.txt", false));
    }
}
