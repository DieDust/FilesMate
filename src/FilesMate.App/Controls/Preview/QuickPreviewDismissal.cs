using FilesMate.App.Controls.FileSurface;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.Preview;

/// <summary>Shared input lifetime for folder and search-result previews.</summary>
internal sealed class QuickPreviewDismissal : IDisposable
{
    private readonly UIElement _root;
    private readonly Action _dismiss;
    private bool _disposed;

    internal QuickPreviewDismissal(UIElement root, Action dismiss)
    {
        _root = root;
        _dismiss = dismiss;
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed), true);
        root.GotFocus += GotFocus;
    }

    internal void DismissOutside(DependencyObject? source, bool leftButton = true)
    {
        if (_disposed) return;
        // Let selection follow a clicked file before deciding whether to close.
        if (leftButton && Ancestors(source).Any(node => node is FileRow { EntryId: >= 0 } or FileTile { EntryId: >= 0 })) return;
        _dismiss();
    }

    private void PointerPressed(object sender, PointerRoutedEventArgs e) =>
        DismissOutside(e.OriginalSource as DependencyObject, e.GetCurrentPoint(null).Properties.IsLeftButtonPressed);

    private void GotFocus(object sender, RoutedEventArgs e) =>
        _root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_disposed || _root.XamlRoot is not { } root) return;
            var focused = FocusManager.GetFocusedElement(root) as DependencyObject;
            if (focused is not null && !Ancestors(focused).Any(node => node is FileDetailsSurface)) _dismiss();
        });

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? node)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node)) yield return node;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed));
        _root.GotFocus -= GotFocus;
    }
}
