using FilesMate.App.Navigation;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private void SyncInlineFilters()
    {
        if (_disposed) return;
        InlineFilters.SetPane(ViewModel);
        Omni.SetInlineFolderSearch(!HomeLocation.IsHome(ViewModel.AddressText), ViewModel.FilterQuery, ViewModel.IsRecursive);
    }
}
