using FilesMate.Core.Operations;

namespace FilesMate.App.Commands;

public static class NewDocumentCommands
{
    public static readonly AppCommandId[] All = [AppCommandId.NewFile, AppCommandId.NewWordFile, AppCommandId.NewSpreadsheetFile,
        AppCommandId.NewPresentationFile, AppCommandId.NewMarkdownFile, AppCommandId.NewCsvFile, AppCommandId.NewRichTextFile,
        AppCommandId.NewHtmlFile, AppCommandId.NewJsonFile];
    public static bool IsDocument(AppCommandId id) => Array.IndexOf(All, id) >= 0;
    public static NewDocumentKind Kind(AppCommandId id) => id switch
    {
        AppCommandId.NewWordFile => NewDocumentKind.Word, AppCommandId.NewSpreadsheetFile => NewDocumentKind.Spreadsheet,
        AppCommandId.NewPresentationFile => NewDocumentKind.Presentation, AppCommandId.NewMarkdownFile => NewDocumentKind.Markdown,
        AppCommandId.NewCsvFile => NewDocumentKind.Csv, AppCommandId.NewRichTextFile => NewDocumentKind.RichText,
        AppCommandId.NewHtmlFile => NewDocumentKind.Html, AppCommandId.NewJsonFile => NewDocumentKind.Json, _ => NewDocumentKind.Text
    };
    public static string LabelKey(AppCommandId id) => "NewDocument_" + Kind(id);
}
