namespace FilesMate.App.Navigation;

public interface IAppPageFactory
{
    public PageCreationResult<T> Create<T>(Func<T> create)
        where T : class;
}

public readonly record struct PageCreationResult<T>(T? Page, string? Error)
    where T : class
{
    public bool Succeeded => Page is not null;
}
