using FilesMate.App.Localization;
using FilesMate.Core.Recursive;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileRow
{
    private bool _recursiveRow;
    private bool _recursiveIdentityVerified = true;
    private string _recursiveRelativeParent = string.Empty;
    private int _recursiveDepth;
    private Action<int>? _toggleRecursive;
    private Action<int>? _expandRecursiveAlt;
    private int _arrowPressEntryId = -1;
    private bool _arrowPressAlt;

    private void RecursiveArrow_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        RecordRecursiveArrow("Pressed", args);
        if (!args.GetCurrentPoint(RecursiveToggle).Properties.IsLeftButtonPressed) return;
        _arrowPressEntryId = EntryId;
        _arrowPressAlt = args.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Menu);
    }

    internal void SetRecursivePresentation(RecursiveNode? node, bool expanded, bool contextOnly,
        Action<int>? toggle, string relativeParent = "", bool identityVerified = true, Action<int>? altExpand = null, RecursiveChildState childState = RecursiveChildState.Unknown, bool childrenCurrent = true)
    {
        _recursiveRow = node is not null;
        _recursiveIdentityVerified = node is null || identityVerified;
        _toggleRecursive = toggle;
        _expandRecursiveAlt = toggle is null ? null : altExpand;
        _recursiveRelativeParent = relativeParent;
        _recursiveDepth = node?.Depth ?? 0;
        UpdateRecursiveLeadingLayout();
        NameText.Opacity = contextOnly ? 0.65 : 1;
        if (node is not { } value)
        {
            RecursiveToggle.Visibility = Visibility.Collapsed;
            RecursiveToggle.IsEnabled = false;
            UpdateExtraCells();
            return;
        }
        CancelFolderSize();
        RecursiveToggle.Visibility = value.IsDirectory && childState != RecursiveChildState.Empty ? Visibility.Visible : Visibility.Collapsed;
        RecursiveToggle.IsEnabled = value.IsDirectory && childState is not (RecursiveChildState.Empty or RecursiveChildState.Unavailable or RecursiveChildState.Error) && !value.TraversalSkipped && toggle is not null;
        expanded = expanded && childState == RecursiveChildState.HasChildren;
        RecursiveChevron.Glyph = expanded ? "\uE70D" : "\uE76C";
        AutomationProperties.SetName(RecursiveToggle, StringTable.Get(expanded ? "Recursive_Collapse" : "Recursive_Expand"));
        ToolTipService.SetToolTip(RecursiveToggle, StringTable.Get(value.TraversalSkipped ? "Recursive_NotTraversed"
            : childState is RecursiveChildState.Unknown or RecursiveChildState.Loading
                ? !childrenCurrent ? "Recursive_ChildrenStale" : childState == RecursiveChildState.Loading ? "Recursive_ChildrenLoading" : "Recursive_ChildrenPending"
            : expanded ? "Recursive_Collapse" : "Recursive_Expand"));
        UpdateExtraCells();
    }

    private void UpdateRecursiveLeadingLayout()
    {
        var indent = _recursiveRow ? _recursiveDepth * FileColumnLayout.RecursiveIndentWidth : 0;
        var arrow = _recursiveRow ? FileColumnLayout.RecursiveArrowWidth : 0;
        if (RecursiveIndentColumn.Width.Value != indent) RecursiveIndentColumn.Width = new GridLength(indent);
        if (RecursiveArrowColumn.Width.Value != arrow) RecursiveArrowColumn.Width = new GridLength(arrow);
        if (LeadingIconColumn.Width.Value != _glyphWidth) LeadingIconColumn.Width = new GridLength(_glyphWidth);
        var height = Math.Max(20, IconFrame.Height);
        if (RecursiveToggle.Height != height) RecursiveToggle.Height = height;
    }

    private static int _recursiveArrowAuditCount;
    private void RecordRecursiveArrow(string stage, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs? args = null)
    {
        if (!FilesMate.Core.Configuration.RuntimeProfile.IsIsolated || _recursiveArrowAuditCount >= 64
            || !File.Exists(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "presentation-audit.enabled"))) return;
        ++_recursiveArrowAuditCount;
        try
        {
            var point = args?.GetCurrentPoint(RecursiveToggle);
            var data = new { Utc = DateTimeOffset.UtcNow, Stage = stage, EntryId, ViewIndex,
                Recursive = _recursiveRow, Enabled = RecursiveToggle.IsEnabled, Glyph = RecursiveChevron.Glyph,
                Alt = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                    & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0,
                EventModifiers = args?.KeyModifiers.ToString(), CapturedAlt = _arrowPressAlt, PressEntry = _arrowPressEntryId, AltHandler = _expandRecursiveAlt is not null, ToggleHandler = _toggleRecursive is not null,
                AltDepth = App.ExplorerPreferences.RecursiveAltExpandDepth,
                OriginalSource = args?.OriginalSource?.GetType().Name,
                LeftDown = point?.Properties.IsLeftButtonPressed, Position = point?.Position };
            File.AppendAllText(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "recursive-arrow.jsonl"),
                System.Text.Json.JsonSerializer.Serialize(data) + Environment.NewLine);
        }
        catch (Exception error) { App.AppendCrashRecord("RecursiveArrowAudit", error); }
    }

    private void RecursiveToggle_Click(object sender, RoutedEventArgs e)
    {
        RecordRecursiveArrow("Click");
        if (EntryId < 0 || !_recursiveRow) return;
        // Capture Alt from the actual pointer event, before focus/key state can
        // change. A recycled row cannot apply an old press to its new identity.
        if (_arrowPressEntryId >= 0 && _arrowPressEntryId != EntryId) { _arrowPressEntryId = -1; return; }
        var alt = _arrowPressEntryId >= 0 ? _arrowPressAlt
            : (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        _arrowPressEntryId = -1;
        if (alt && _expandRecursiveAlt is not null) _expandRecursiveAlt(EntryId);
        else _toggleRecursive?.Invoke(EntryId);
    }
}
