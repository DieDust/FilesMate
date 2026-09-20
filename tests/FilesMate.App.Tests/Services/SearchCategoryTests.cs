using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Services;

public sealed class SearchCategoryTests
{
    [Fact]
    public void Extensions_accept_common_spellings_without_duplicates()
        => Assert.Equal(new[] { ".jpg", ".png", ".webp" }, SearchCategories.ParseExtensions("JPG, .jpg；*.PNG webp"));

    [Theory]
    [InlineData("C:\\pictures")]
    [InlineData("jpg/*")]
    [InlineData("")]
    public void Invalid_extensions_are_rejected(string value)
        => Assert.Throws<ArgumentException>(() => SearchCategories.ParseExtensions(value));

    [Fact]
    public void Order_visibility_and_custom_suffixes_survive_reload()
    {
        var profile = Path.Combine(Path.GetTempPath(), "filesmate-category-" + Guid.NewGuid().ToString("N"));
        try
        {
            var custom = new SearchCategory("photos", "我的图片", null, [".jpg", ".png"]);
            var order = new[] { custom }.Concat(SearchCategories.Defaults().AsEnumerable().Reverse().Select(c => c with { Visible = c.Builtin != SearchFilter.Apps })).ToList();
            SearchCategories.Save(profile, order);
            var restored = SearchCategories.Load(profile);
            Assert.Equal(order.Select(c => c.Id), restored.Select(c => c.Id));
            Assert.False(restored.Single(c => c.Builtin == SearchFilter.Apps).Visible);
            Assert.Equal(custom.Extensions, restored[0].Extensions);
        }
        finally { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
    }

    [Fact]
    public async Task Extension_filter_runs_before_paging_and_excludes_directories()
    {
        var profile = Path.Combine(Path.GetTempPath(), "filesmate-category-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        try
        {
            var db = GlobalSearchConfiguration.ResolveDatabase(profile);
            using (var connection = new SqliteConnection($"Data Source={db};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
                command.ExecuteNonQuery();
                for (var i = 0; i < 150; i++)
                {
                    command.CommandText = "INSERT INTO file_name VALUES ($name,$path,$dir)";
                    command.Parameters.Clear();
                    var name = $"photo_{i}." + (i % 2 == 0 ? "JPG" : "txt");
                    command.Parameters.AddWithValue("$name", name);
                    command.Parameters.AddWithValue("$path", Path.Combine(profile, name));
                    command.Parameters.AddWithValue("$dir", i == 0 ? "1" : "0");
                    command.ExecuteNonQuery();
                }
            }
            var provider = new IndexedFilesSearchProvider(profile, true, [".jpg"]);
            var paths = new List<string>();
            for (var offset = 0; ; offset += 17)
            {
                var response = await provider.SearchAsync("photo", default, limit: 17, offset: offset);
                Assert.All(response.Hits, h => Assert.False(h.IsDirectory));
                paths.AddRange(response.Hits.Select(h => h.Path));
                if (!response.HasMore) break;
            }
            Assert.Equal(74, paths.Count);
            Assert.Equal(74, paths.Distinct().Count());
            Assert.All(paths, p => Assert.EndsWith(".JPG", p));
        }
        finally { Directory.Delete(profile, true); }
    }
}
