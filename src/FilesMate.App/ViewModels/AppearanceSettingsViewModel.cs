using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.App.ViewModels;

public sealed class AppearanceSettingsViewModel
{
    private readonly IAppearanceSettingsService _store;
    private readonly Action<AppearanceSettings>? _apply;

    public AppearanceSettingsViewModel(
        IAppearanceSettingsService store,
        AppearanceSettings initial,
        Action<AppearanceSettings>? apply = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _apply = apply;
        Current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public AppearanceSettings Current { get; private set; }

    public string? ErrorText { get; private set; }

    public event Action? Changed;

    public Task SetThemeAndAccentAsync(AppThemeKind theme, AccentKind accent, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { Theme = theme, Accent = accent }, cancellationToken);

    public Task SetThemeAsync(AppThemeKind theme, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { Theme = theme }, cancellationToken);

    public Task SetBackdropAsync(BackdropKind backdrop, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { Backdrop = backdrop }, cancellationToken);

    public Task SetShowStatusBarAsync(bool show, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { ShowStatusBar = show }, cancellationToken);

    public Task SetShowToolbarAsync(bool show, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { ShowToolbar = show }, cancellationToken);

    public Task SetUseBundledFileIconsAsync(bool useBundled, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { UseBundledFileIcons = useBundled }, cancellationToken);

    public Task SetReduceMotionAsync(ReduceMotionKind reduce, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { ReduceMotion = reduce }, cancellationToken);

    public Task SetGlassEffectAsync(GlassEffectMode glassEffect, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { GlassEffect = glassEffect }, cancellationToken);

    public Task SetShellStyleAsync(ShellStyleKind style, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { ShellStyle = style }, cancellationToken);

    public void PreviewTransparencyPercent(int percent) =>
        _apply?.Invoke(Current with { TransparencyPercent = Math.Clamp(percent, 0, 100) });

    public Task SetTransparencyPercentAsync(int percent, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { TransparencyPercent = Math.Clamp(percent, 0, 100) }, cancellationToken);

    public Task SetAccentAsync(AccentKind accent, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { Accent = accent }, cancellationToken);

    public Task SetCustomAccentAsync(string? customAccent, CancellationToken cancellationToken = default) =>
        CommitAsync(Current with { Accent = AccentKind.Custom, CustomAccent = customAccent }, cancellationToken);

    private async Task CommitAsync(AppearanceSettings next, CancellationToken cancellationToken)
    {
        var previous = Current;
        Current = next;
        try
        {
            _apply?.Invoke(next);
            await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            ErrorText = null;
        }
        catch (Exception)
        {
            Current = previous;
            try
            {
                _apply?.Invoke(previous);
            }
            catch (Exception)
            {
            }

            ErrorText = StringTable.Get("Error_SaveAppearance");
        }

        Changed?.Invoke();
    }
}
