using FilesMate.Search;

namespace FilesMate.App.Tests.Services;

public sealed class SearchPreviewSizeTests
{
    [Fact]
    public void Size_persists_across_loads_and_recovers_from_invalid_input()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-preview-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(SearchPreviewSize.Load(root));
            new SearchPreviewSize(620, 740).Save(root);
            Assert.Equal(new SearchPreviewSize(620, 740), SearchPreviewSize.Load(root));
            Assert.Equal(new SearchPreviewSize(240, 1600), new SearchPreviewSize(-20, 5000).Normalize());
            Assert.Equal(new SearchPreviewSize(400, 368), new SearchPreviewSize(double.NaN, double.PositiveInfinity).Normalize());
            File.WriteAllText(Path.Combine(root, "search-preview-size.json"), "broken");
            Assert.Null(SearchPreviewSize.Load(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
