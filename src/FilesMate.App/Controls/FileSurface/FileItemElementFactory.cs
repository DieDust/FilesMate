using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace FilesMate.App.Controls.FileSurface;

// Keep pooling in managed code without DataTemplate's native origin-template
// links. Recycled children are detached after the repeater finishes its native
// child traversal; removing one inside RecycleElement skips the next child.
internal sealed class FileItemElementFactory(bool tiles) : IElementFactory
{
    private const int RetainedLimit = 128;
    private readonly Stack<UIElement> _available = new();
    private readonly HashSet<UIElement> _pendingDetach = new();
    private bool _detachScheduled;
    private bool _retired;

    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        if (_retired) throw new InvalidOperationException("Retired file presenter factory.");
        if (!_available.TryPop(out var element)) return tiles ? new FileTile() : new FileRow();
        // A same-turn reuse is still attached to this repeater. Its new binding
        // owns the element now, so the queued cleanup must leave it attached.
        _pendingDetach.Remove(element);
        return element;
    }

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
        var element = args.Element;
        // ElementClearing has already revoked binding/work and hidden the row.
        // WinUI enumerates Children forwards during Reset and auto-recycling.
        // Keep that collection stable until its call stack has returned.
        if (!_retired && _available.Count < RetainedLimit)
            _available.Push(element);
        else ClearPresenter(element);

        if (VisualTreeHelper.GetParent(element) is not UIElement) return;
        _pendingDetach.Add(element);
        if (!_detachScheduled)
            _detachScheduled = element.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low, DetachRecycledElements);
    }

    private void DetachRecycledElements()
    {
        _detachScheduled = false;
        foreach (var element in _pendingDetach.ToArray())
        {
            // Removing another child can raise Unloaded and reenter binding.
            // Recheck ownership individually, including any same-turn reuse.
            if (!_pendingDetach.Remove(element)) continue;
            if (VisualTreeHelper.GetParent(element) is UIElement parent)
                GetChildren(parent).Remove(element);
        }
    }

    internal void Retire()
    {
        _retired = true;
        foreach (var element in _available) ClearPresenter(element);
        _available.Clear();
        // Pending detach still runs outside native enumeration. The surface's
        // final ClearChildren also handles a dispatcher that is shutting down.
    }

    internal static void ClearChildren(ItemsRepeater repeater) => GetChildren(repeater).Clear();

    internal static UIElementCollection PresentationChildren(ItemsRepeater repeater) => GetChildren(repeater);

    private static void ClearPresenter(UIElement element)
    {
        if (element is FileRow row) row.Clear();
        else if (element is FileTile tile) tile.Clear();
    }

    // ItemsRepeater projects as FrameworkElement, while its native inner object
    // is a Panel (the same base used by WinUI's built-in recycle factory).
    // The protected projection constructor wraps that existing native object.
    private static UIElementCollection GetChildren(UIElement owner) =>
        new NativePanel(((IWinRTObject)owner).NativeObject).Children;

    private sealed class NativePanel(IObjectReference owner) : Panel(owner);
}

