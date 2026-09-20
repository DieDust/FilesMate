using FilesMate.App.Animations;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Tests.DesignSystem;
using FilesMate.App.ViewModels;

namespace FilesMate.App.Tests.Settings;

public sealed class AppearanceSettingsTests
{
    [Fact]
    public async Task File_icons_default_on_and_round_trip_without_changing_other_appearance()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-icon-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "appearance.json");
            File.WriteAllText(path, "{\"theme\":\"Dark\"}");
            var store = new AppearanceSettingsService(path);
            var initial = store.Load();
            Assert.True(initial.UseBundledFileIcons);
            var applied = new List<AppearanceSettings>();
            var vm = new AppearanceSettingsViewModel(store, initial, applied.Add);
            await vm.SetUseBundledFileIconsAsync(false);
            Assert.Equal(initial with { UseBundledFileIcons = false }, store.Load());
            Assert.False(applied.Last().UseBundledFileIcons);
            await vm.SetUseBundledFileIconsAsync(true);
            Assert.Equal(initial, store.Load());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0, AppThemeKind.Dark)]
    [InlineData(1, AppThemeKind.Light)]
    [InlineData(2, AppThemeKind.Light)]
    [InlineData("0", AppThemeKind.Dark)]
    [InlineData(null, AppThemeKind.Light)]
    public void Windows_app_theme_value_resolves_system_selection(object? value, AppThemeKind expected) =>
        Assert.Equal(expected, SystemThemePreference.FromAppsUseLightTheme(value));

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("1")]
    public void Old_or_invalid_style_settings_keep_layered_default(string? style)
    {
        Assert.Equal(ShellStyleKind.Layered, AppearanceSettings.Sanitize(null, null, null, null, null, shellStyle: style).ShellStyle);
    }

    [Fact]
    public async Task Shell_style_round_trips_and_can_switch_back_without_changing_theme()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "appearance.json");
        try
        {
            var store = new AppearanceSettingsService(file);
            var applied = new List<AppearanceSettings>();
            var initial = AppearanceSettings.Default with { Theme = AppThemeKind.Dark, Accent = AccentKind.Gold };
            var vm = new AppearanceSettingsViewModel(store, initial, applied.Add);
            await vm.SetShellStyleAsync(ShellStyleKind.Unified);
            Assert.Equal(initial with { ShellStyle = ShellStyleKind.Unified }, store.Load());
            await vm.SetShellStyleAsync(ShellStyleKind.Layered);
            Assert.Equal(initial, store.Load());
            Assert.Equal(new[] { ShellStyleKind.Unified, ShellStyleKind.Layered }, applied.Select(s => s.ShellStyle));
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(file))) Directory.Delete(Path.GetDirectoryName(file)!, true); }
    }

    [Fact]
    public void Missing_or_corrupt_json_returns_defaults()
    {
        var missing = new AppearanceSettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "appearance.json"));
        Assert.Equal(AppearanceSettings.Default, missing.Load());
        Assert.Equal(GlassEffectMode.Balanced, missing.Load().GlassEffect);

        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{ not json");
        var corrupt = new AppearanceSettingsService(file);
        Assert.Equal(AppearanceSettings.Default, corrupt.Load());
    }

    [Fact]
    public void Illegal_values_fall_back_per_field()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(
            file,
            """
            {
              "theme": "Neon",
              "backdrop": "Prism",
              "showStatusBar": false,
              "showToolbar": false,
              "reduceMotion": "Off",
              "glassEffect": "Molten"
            }
            """);

        var loaded = new AppearanceSettingsService(file).Load();
        Assert.Equal(AppThemeKind.System, loaded.Theme);
        Assert.Equal(BackdropKind.Acrylic, loaded.Backdrop);
        Assert.False(loaded.ShowStatusBar);
        Assert.False(loaded.ShowToolbar);
        Assert.Equal(ReduceMotionKind.System, loaded.ReduceMotion);
        Assert.Equal(GlassEffectMode.Balanced, loaded.GlassEffect);
        Assert.Equal(
            AppearanceSettings.Default.Theme,
            AppearanceSettings.Sanitize("1", "Acrylic", true, true, "System", "Immersive").Theme);
        Assert.Equal(
            GlassEffectMode.Immersive,
            AppearanceSettings.Sanitize("System", "Acrylic", true, true, "System", "Immersive").GlassEffect);
        Assert.Equal(
            BackdropKind.Mica,
            AppearanceSettings.Sanitize("System", "Mica", true, true, "System", "Balanced").Backdrop);
        Assert.Equal(
            AccentKind.Gold,
            AppearanceSettings.Sanitize("System", "Acrylic", true, true, "System", "Balanced", "Gold").Accent);
    }

    [Fact]
    public async Task Save_replaces_via_temp_file_and_round_trips()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "appearance.json");
        var service = new AppearanceSettingsService(file);
        var settings = new AppearanceSettings(
            AppThemeKind.Dark,
            BackdropKind.Solid,
            ShowStatusBar: false,
            ShowToolbar: false,
            ReduceMotionKind.On,
            GlassEffectMode.Immersive);

        await service.SaveAsync(settings);

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        Assert.Contains("\"glassEffect\": \"Immersive\"", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
        Assert.Contains("File.Replace", File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Services",
            "AppearanceSettingsService.cs")), StringComparison.Ordinal);
        Assert.Equal(settings, service.Load());
    }

    [Fact]
    public void Reduce_motion_on_cannot_override_system_off()
    {
        Assert.False(ReduceMotionGate.AnimationsEnabled(systemEnabled: false, ReduceMotionKind.System));
        Assert.False(ReduceMotionGate.AnimationsEnabled(systemEnabled: false, ReduceMotionKind.On));
        Assert.True(ReduceMotionGate.AnimationsEnabled(systemEnabled: true, ReduceMotionKind.System));
        Assert.False(ReduceMotionGate.AnimationsEnabled(systemEnabled: true, ReduceMotionKind.On));
    }

    [Fact]
    public async Task View_model_applies_immediately_and_rolls_back_when_save_fails()
    {
        var applied = new List<AppearanceSettings>();
        var store = new FakeStore { LoadResult = AppearanceSettings.Default, ThrowOnSave = true };
        var vm = new AppearanceSettingsViewModel(store, AppearanceSettings.Default, applied.Add);

        await vm.SetThemeAsync(AppThemeKind.Light);

        Assert.Equal(AppearanceSettings.Default, vm.Current);
        Assert.Equal("Couldn't save appearance settings.", vm.ErrorText);
        Assert.Equal([AppearanceSettings.Default with { Theme = AppThemeKind.Light }, AppearanceSettings.Default], applied);
        Assert.DoesNotContain("Page", File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Services",
            "AppearanceSettingsService.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI", File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "ViewModels",
            "AppearanceSettingsViewModel.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task View_model_applies_glass_mode_immediately()
    {
        var applied = new List<AppearanceSettings>();
        var store = new FakeStore { LoadResult = AppearanceSettings.Default };
        var vm = new AppearanceSettingsViewModel(store, AppearanceSettings.Default, applied.Add);

        await vm.SetGlassEffectAsync(GlassEffectMode.Immersive);

        var expected = AppearanceSettings.Default with { GlassEffect = GlassEffectMode.Immersive };
        Assert.Equal(expected, vm.Current);
        Assert.Equal([expected], applied);
        Assert.Null(vm.ErrorText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_theme_and_accent_save_together_and_preserve_other_options(bool failure)
    {
        var initial = AppearanceSettings.Default with { Backdrop = BackdropKind.Solid, ShowToolbar = false };
        var vm = new AppearanceSettingsViewModel(new FakeStore { LoadResult = initial, ThrowOnSave = failure }, initial);
        await vm.SetThemeAndAccentAsync(AppThemeKind.Dark, AccentKind.Gold);
        Assert.Equal(failure ? initial : initial with { Theme = AppThemeKind.Dark, Accent = AccentKind.Gold }, vm.Current);
        Assert.Equal(failure, vm.ErrorText is not null);
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 0)]
    [InlineData(57, 57)]
    [InlineData(100, 100)]
    [InlineData(130, 100)]
    public async Task Transparency_round_trips_and_clamps_out_of_range_values(int input, int expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppearanceSettingsService(Path.Combine(directory, "appearance.json"));
        try
        {
            await service.SaveAsync(AppearanceSettings.Default with { TransparencyPercent = input });
            Assert.Equal(expected, service.Load().EffectiveTransparencyPercent);
            Assert.Equal(expected, service.Load().TransparencyPercent);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(GlassEffectMode.Balanced, 18)]
    [InlineData(GlassEffectMode.Immersive, 32)]
    [InlineData(GlassEffectMode.Off, 0)]
    public void Older_settings_keep_their_preset_transparency(GlassEffectMode effect, int expected)
    {
        var settings = AppearanceSettings.Sanitize(null, null, null, null, null, effect.ToString());
        Assert.Null(settings.TransparencyPercent);
        Assert.Equal(expected, settings.EffectiveTransparencyPercent);
    }

    [Fact]
    public async Task Transparency_preview_does_not_save_and_style_changes_preserve_the_saved_value()
    {
        var applied = new List<AppearanceSettings>();
        var store = new FakeStore { LoadResult = AppearanceSettings.Default };
        var vm = new AppearanceSettingsViewModel(store, AppearanceSettings.Default, applied.Add);
        for (var percent = 0; percent <= 100; percent++) vm.PreviewTransparencyPercent(percent);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(100, applied[^1].TransparencyPercent);
        Assert.Null(vm.Current.TransparencyPercent);
        await vm.SetTransparencyPercentAsync(65);
        await vm.SetShellStyleAsync(ShellStyleKind.Unified);
        await vm.SetShellStyleAsync(ShellStyleKind.Layered);
        Assert.Equal(65, vm.Current.TransparencyPercent);
        Assert.Equal(3, store.SaveCount);
    }

    [Fact]
    public async Task Failed_transparency_save_restores_previous_value_and_rendering()
    {
        var initial = AppearanceSettings.Default with { TransparencyPercent = 40 };
        var applied = new List<AppearanceSettings>();
        var vm = new AppearanceSettingsViewModel(new FakeStore { LoadResult = initial, ThrowOnSave = true }, initial, applied.Add);
        vm.PreviewTransparencyPercent(90);
        await vm.SetTransparencyPercentAsync(90);
        Assert.Equal(initial, vm.Current);
        Assert.Equal(initial, applied[^1]);
        Assert.NotNull(vm.ErrorText);
    }

    private sealed class FakeStore : IAppearanceSettingsService
    {
        public required AppearanceSettings LoadResult { get; init; }

        public bool ThrowOnSave { get; init; }
        public int SaveCount { get; private set; }

        public AppearanceSettings Load() => LoadResult;

        public Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return ThrowOnSave
                ? throw new IOException("disk full")
                : Task.CompletedTask;
        }
    }
}
