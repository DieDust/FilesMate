using FilesMate.App.Shortcuts;

namespace FilesMate.App.Tests.Settings;

public sealed class ShortcutMapTests
{
    [Fact]
    public void Defaults_are_unique_and_human_readable()
    {
        var map = new ShortcutMap();
        var values = ShortcutDefaults.Definitions.Select(item => map[item.Action]).ToArray();

        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal("Ctrl+T", map[ShortcutAction.NewTab].DisplayText);
        Assert.Equal("Ctrl+Z", map[ShortcutAction.Undo].DisplayText);
        Assert.Equal("Ctrl+Y", map[ShortcutAction.Redo].DisplayText);
        Assert.Equal("Shift+Delete", map[ShortcutAction.PermanentDelete].DisplayText);
    }

    [Fact]
    public void Conflict_is_rejected_without_changing_the_map()
    {
        var map = new ShortcutMap();
        var copy = map[ShortcutAction.Copy];

        Assert.False(map.TrySet(ShortcutAction.Paste, copy, out var conflict));
        Assert.Equal(ShortcutAction.Copy, conflict);
        Assert.Equal("Ctrl+V", map[ShortcutAction.Paste].DisplayText);
    }

    [Fact]
    public async Task Settings_round_trip_and_corrupt_files_fall_back_to_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "shortcuts.json");
        try
        {
            var service = new ShortcutSettingsService(path);
            var map = new ShortcutMap();
            Assert.True(map.TrySet(
                ShortcutAction.NewTab,
                new ShortcutGesture(ShortcutKey.N, ShortcutModifiers.Control | ShortcutModifiers.Menu),
                out _));

            await service.SaveAsync(map);
            Assert.Equal("Ctrl+Alt+N", service.Load()[ShortcutAction.NewTab].DisplayText);

            await File.WriteAllTextAsync(path, "not json");
            Assert.Equal("Ctrl+T", service.Load()[ShortcutAction.NewTab].DisplayText);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Reset_restores_defaults()
    {
        var map = new ShortcutMap();
        Assert.True(map.TrySet(
            ShortcutAction.NewTab,
            new ShortcutGesture(ShortcutKey.N, ShortcutModifiers.Control | ShortcutModifiers.Menu),
            out _));

        map.Reset();

        Assert.Equal("Ctrl+T", map[ShortcutAction.NewTab].DisplayText);
    }
}
