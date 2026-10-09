using System.Xml;
using FilesMate.App.Preview;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace FilesMate.SearchHost;

internal static class PreviewHighlighting
{
    // A skin picker can produce many distinct palettes in one preview session.
    private const int MaximumDefinitions = 16;
    private static readonly Dictionary<CodePreviewPalette, IHighlightingDefinition> Definitions = new();
    internal static IHighlightingDefinition? ForPath(string path, uint background)
    {
        if (System.Windows.SystemParameters.HighContrast || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)) return null;
        var palette = CodePreviewPalette.Resolve(background);
        if (Definitions.TryGetValue(palette, out var cached)) return cached;
        var xml = $$"""
            <SyntaxDefinition name="FilesMate" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="String" foreground="#{{palette.String & 0xFFFFFF:X6}}"/>
              <Color name="Number" foreground="#{{palette.Number & 0xFFFFFF:X6}}"/>
              <Color name="Keyword" foreground="#{{palette.Keyword & 0xFFFFFF:X6}}"/>
              <Color name="Comment" foreground="#{{palette.Comment & 0xFFFFFF:X6}}"/>
              <Color name="Error" foreground="#{{palette.Error & 0xFFFFFF:X6}}" fontWeight="bold"/>
              <RuleSet>
                <Span color="Comment" begin="//"/>
                <Span color="Comment" begin="/\*" end="\*/" multiline="true"/>
                <Span color="Comment" begin="&lt;!--" end="--&gt;" multiline="true"/>
                <Span color="String" begin="&quot;" end="&quot;"><RuleSet><Rule>\\.</Rule></RuleSet></Span>
                <Span color="String" begin="'" end="'"><RuleSet><Rule>\\.</Rule></RuleSet></Span>
                <Rule color="Number">\b[0-9]+(?:[.:-][0-9]+)*\b</Rule>
                <Rule color="Keyword">&lt;/?[a-zA-Z_][\w:.-]*|\b(?:true|false|null|return|public|private|class|using|namespace|async|await|var|const|let|function|import|from|def|if|else|for|while|new|void|int|string|INFO|DEBUG|Info|Debug)\b</Rule>
                <Rule color="Error">\b(?:ERROR|Error|error|WARN|Warning|warning|Exception|FATAL)\b</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        if (Definitions.Count >= MaximumDefinitions) Definitions.Clear();
        return Definitions[palette] = definition;
    }
}
