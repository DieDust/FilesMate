using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    // Disabled for normal users; an isolated native acceptance run may request
    // bounded numeric/binding evidence without putting diagnostics in the UI.
    private readonly string? _presentationAudit = ResolvePresentationAudit();

    private static string? ResolvePresentationAudit()
    {
        // Shell-mediated independent relaunches may not retain a tool's environment.
        // An explicit marker belongs only to the fresh isolated acceptance profile.
        var profile = FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath;
        return FilesMate.Core.Configuration.RuntimeProfile.IsIsolated
            && File.Exists(Path.Combine(profile, "presentation-audit.enabled"))
            ? Path.Combine(profile, "presentation-audit.jsonl") : null;
    }
    private bool _presentationAuditPending;

    private void SchedulePresentationAudit()
    {
        if (string.IsNullOrWhiteSpace(_presentationAudit) || _presentationAuditPending || _resourcesReleased) return;
        _presentationAuditPending = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _presentationAuditPending = false;
            if (_resourcesReleased) return;
            try
            {
                var children = FileItemElementFactory.PresentationChildren(Repeater);
                var records = new List<object>(Math.Min(children.Count, 256));
                for (var i = 0; i < Math.Min(children.Count, 256); i++)
                {
                    var element = children[i];
                    var nativeIndex = Repeater.GetElementIndex(element);
                    var row = element as FileRow;
                    var tile = element as FileTile;
                    if (row is null && tile is null) continue;
                    var entry = row?.Entry ?? tile!.Entry;
                    var view = row?.ViewIndex ?? tile!.ViewIndex;
                    var valid = _items.TryGetEntry(nativeIndex, out var current)
                        && view == nativeIndex && current.Id == entry.Id && current.Name == entry.Name;
                    records.Add(new
                    {
                        NativeIndex = nativeIndex, ViewIndex = view, EntryId = row?.EntryId ?? tile!.EntryId,
                        Name = entry.Name, Visible = element.Visibility == Visibility.Visible,
                        Selected = _selection.Contains(entry.Id),
                        element.IsHitTestVisible, ValidBinding = valid,
                        Width = (element as FrameworkElement)?.ActualWidth,
                        Height = (element as FrameworkElement)?.ActualHeight
                    });
                }
                var data = new
                {
                    Utc = DateTimeOffset.UtcNow, Generation = _generation, BindingVersion = _bindingVersion,
                    Revision = _items.Revision, ItemsSourceCount = _items.Count, NativeChildCount = children.Count,
                    RecursiveSessionId = _items.Binding?.Recursive?.SessionId,
                    DiagnosticOperation = _items.Binding?.DiagnosticOperation,
                    PrimaryId = _selection.PrimaryId, AnchorId = _selection.AnchorId,
                    Scroller.ExtentHeight, Scroller.ExtentWidth, Scroller.ViewportHeight,
                    Scroller.VerticalOffset, Scroller.HorizontalOffset, Layout = _layout.ToString(),
                    Rows = records, Truncated = children.Count > 256
                };
                File.AppendAllText(_presentationAudit!, JsonSerializer.Serialize(data) + Environment.NewLine);
            }
            catch (Exception error) { App.AppendCrashRecord("PresentationAudit", error); }
        })) _presentationAuditPending = false;
    }
}
