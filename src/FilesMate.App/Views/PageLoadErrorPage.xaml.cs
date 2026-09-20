using FilesMate.App.Localization;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel.DataTransfer;

namespace FilesMate.App.Views;

public sealed partial class PageLoadErrorPage : Page
{
    private readonly string _diagnostics;

    public PageLoadErrorPage()
        : this("unknown", "No diagnostics were provided.")
    {
    }

    public PageLoadErrorPage(string pageKey, string diagnostics)
    {
        PageKey = string.IsNullOrWhiteSpace(pageKey) ? "unknown" : pageKey;
        _diagnostics = string.IsNullOrWhiteSpace(diagnostics)
            ? "No diagnostics were provided."
            : diagnostics;

        InitializeComponent();
        TitleText.Text = StringTable.Get("PageLoadError_Title");
        BodyText.Text = StringTable.Get("PageLoadError_Body");
        CopyButton.Content = StringTable.Get("CopyDiagnostics");
    }

    public string PageKey { get; }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_diagnostics);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        CopyButton.Content = StringTable.Get("DiagnosticsCopied");
    }
}
