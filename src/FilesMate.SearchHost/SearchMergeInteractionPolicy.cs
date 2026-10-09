namespace FilesMate.SearchHost;

/// <summary>Old press targets do not hold a replacement after the physical button is released.</summary>
internal static class SearchMergeInteractionPolicy
{
    internal static bool IsBusy(bool leftButtonPressed, bool pointerOverResults, bool hasPressedItem,
        bool dragging, bool contextMenuOpen) => dragging || contextMenuOpen
        || leftButtonPressed && (hasPressedItem || pointerOverResults);
}
