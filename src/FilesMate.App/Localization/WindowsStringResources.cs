using Windows.ApplicationModel.Resources;

namespace FilesMate.App.Localization;

internal static class WindowsStringResources
{
    public static void Install()
    {
        try
        {
            var loader = ResourceLoader.GetForViewIndependentUse();
            StringTable.Resolver = key =>
            {
                var value = loader.GetString(key);
                return string.IsNullOrEmpty(value) ? null : value;
            };
        }
        catch (Exception)
        {
        }
    }
}
