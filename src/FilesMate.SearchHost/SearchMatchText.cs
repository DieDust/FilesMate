using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace FilesMate.SearchHost;

/// <summary>Accessible literal text with emphasis inherited from the current skin.</summary>
public static class SearchMatchText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SearchMatchText), new PropertyMetadata(string.Empty, Changed));
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(SearchMatchText), new PropertyMetadata(string.Empty, Changed));
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.RegisterAttached(
        "Selected", typeof(bool), typeof(SearchMatchText), new PropertyMetadata(false, Changed));
    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);
    public static string GetQuery(DependencyObject target) => (string)target.GetValue(QueryProperty);
    public static void SetQuery(DependencyObject target, string value) => target.SetValue(QueryProperty, value);
    public static bool GetSelected(DependencyObject target) => (bool)target.GetValue(SelectedProperty);
    public static void SetSelected(DependencyObject target, bool value) => target.SetValue(SelectedProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBlock block) return;
        var text = GetText(block) ?? string.Empty;
        block.Inlines.Clear();
        foreach (var span in SearchPresentationPolicy.MatchSpans(text, GetQuery(block)))
        {
            var run = new Run(text.Substring(span.Start, span.Length));
            if (span.Matched)
            {
                run.FontWeight = FontWeights.SemiBold;
                // Selected text inherits the foreground paired with its fill.
                // Unselected emphasis uses a contrast-checked shared accent.
                if (!GetSelected(block)) run.SetResourceReference(TextElement.ForegroundProperty, "MatchInk");
            }
            block.Inlines.Add(run);
        }
    }
}
