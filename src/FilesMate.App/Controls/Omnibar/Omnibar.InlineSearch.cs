namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar
{
    private bool _inlineFolderSearch, _inlineRecursiveSearch, _syncingInlineSearch;
    public void SetInlineFolderSearch(bool enabled, string query, bool recursive)
    {
        _inlineFolderSearch = enabled;
        _inlineRecursiveSearch = recursive;
        ApplySearchScope(App.ExplorerPreferences.GlobalSearchScope);
        if (!string.Equals(_submittedLocalQuery, query, StringComparison.Ordinal))
        {
            _submittedLocalQuery = query;
            if (!_globalSearchScope)
            {
                _localSearchDraft = query;
                SetSearchText(query);
            }
        }
        ApplySearchLayout();
    }
}
