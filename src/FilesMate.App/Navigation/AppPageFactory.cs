namespace FilesMate.App.Navigation;

public sealed class AppPageFactory : IAppPageFactory
{
    public PageCreationResult<T> Create<T>(Func<T> create)
        where T : class => TryCreate(create);

    public static PageCreationResult<T> TryCreate<T>(Func<T> create)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(create);

        try
        {
            var page = create();
            return page is null
                ? new PageCreationResult<T>(null, "The page factory returned null.")
                : new PageCreationResult<T>(page, null);
        }
        catch (Exception error)
        {
            return new PageCreationResult<T>(null, error.ToString());
        }
    }
}
