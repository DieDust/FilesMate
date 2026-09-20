using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace FilesMate.SearchHost;

internal static class PreviewHighlighting
{
    private static readonly Dictionary<bool, IHighlightingDefinition> Definitions = new();
    internal static IHighlightingDefinition? ForPath(string path, bool dark)
    {
        if (System.Windows.SystemParameters.HighContrast || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)) return null;
        if (Definitions.TryGetValue(dark, out var cached)) return cached;
        var xml = $$"""
            <SyntaxDefinition name="FilesMate" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="String" foreground="{{(dark ? "#CE9178" : "#9B3B08")}}"/>
              <Color name="Number" foreground="{{(dark ? "#B5CEA8" : "#286B37")}}"/>
              <Color name="Keyword" foreground="{{(dark ? "#82AAFF" : "#075AA4")}}"/>
              <Color name="Comment" foreground="{{(dark ? "#8BA888" : "#547548")}}"/>
              <Color name="Error" foreground="{{(dark ? "#FF9898" : "#B32323")}}" fontWeight="bold"/>
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
        return Definitions[dark] = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
