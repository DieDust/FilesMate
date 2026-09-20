using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class AppPageFactoryTests
{
    [Fact]
    public void Factory_returns_error_descriptor_when_page_creation_throws()
    {
        var result = AppPageFactory.TryCreate<object>(
            () => throw new InvalidOperationException("boom"));

        Assert.False(result.Succeeded);
        Assert.Null(result.Page);
        Assert.Contains("boom", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_returns_created_page_without_error()
    {
        var expected = new object();

        var result = AppPageFactory.TryCreate(() => expected);

        Assert.True(result.Succeeded);
        Assert.Same(expected, result.Page);
        Assert.Null(result.Error);
    }
}
