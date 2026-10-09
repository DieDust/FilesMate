using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.Core.Tests.Lifetime;

public sealed class IntegratedExplorerPreferencesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_and_depth_preferences_survive_each_others_updates(bool global)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-integrated-preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "explorer.json");
        var service = new ExplorerPreferencesService(path);
        foreach (var depth in new int?[] { null, 0, 1, 5, int.MaxValue })
        {
            await service.SaveAsync(ExplorerPreferences.Default with
            {
                GlobalSearchScope = global,
                RecursiveAutoExpandDepth = depth,
                FolderOpeningMode = ItemOpeningMode.SingleClick,
                FileOpeningMode = ItemOpeningMode.DoubleClick,
            });
            var restarted = new ExplorerPreferencesService(path).Load();
            Assert.Equal(global, restarted.GlobalSearchScope);
            Assert.Equal(depth ?? 0, restarted.RecursiveAutoExpandDepth);
            Assert.Equal(ItemOpeningMode.SingleClick, restarted.FolderOpeningMode);
            Assert.Equal(ItemOpeningMode.DoubleClick, restarted.FileOpeningMode);
            await service.SaveAsync(restarted with { RecursiveAutoExpandDepth = 2 });
            Assert.Equal(global, service.Load().GlobalSearchScope);
            await service.SaveAsync(service.Load() with { GlobalSearchScope = !global });
            Assert.Equal(2, service.Load().RecursiveAutoExpandDepth);
        }
        // Keep the isolated fixture as reproducible evidence for this integration.
    }

    [Fact]
    public async Task Missing_and_invalid_combined_preferences_use_collapsed_default()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-integrated-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "explorer.json");
        var service = new ExplorerPreferencesService(path);
        await File.WriteAllTextAsync(path, "{}");
        Assert.False(service.Load().GlobalSearchScope);
        Assert.Equal(0, service.Load().RecursiveAutoExpandDepth);
        await File.WriteAllTextAsync(path, "{\"folderSearchDisplay\":\"42\",\"recursiveAutoExpandDepth\":6,\"folderOpeningMode\":\"SingleClick\"}");
        var settings = service.Load();
        Assert.False(settings.GlobalSearchScope);
        Assert.Equal(0, settings.RecursiveAutoExpandDepth);
        Assert.Equal(ItemOpeningMode.SingleClick, settings.FolderOpeningMode);
    }
}
