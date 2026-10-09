using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Diagnostics;

namespace FilesMate.App.ViewModels;

public sealed class AppearanceSettingsViewModel
{
    private readonly IAppearanceSettingsService _store;
    private readonly Action<AppearanceSettings>? _apply;
    private readonly SemaphoreSlim _commitGate = new(1, 1);
    private SkinPackage? _previewSkin;
    private SkinPackage? _pendingSkinSelection;
    private long _skinSelectionRevision;
    private TaskCompletionSource? _commitsIdle;
    private int? _previewTransparency;
    private long _previewRevision;
    private long _transparencyRevision;
    private SkinHistory? _previousSkin;
    private static readonly Guid LegacyPreviewOwner = Guid.Empty;
    private long _previewEpoch;
    private int _pendingPreviewCommits;
    private bool _pendingSkinDraft;
    private int _pendingCommits;
    private int _pendingSkinCommits;
    private int _pendingSelectionOperations;
    private readonly List<Func<AppearanceSettings, AppearanceSettings>> _uncommittedOrdinaryIntents = [];

    public Guid? CurrentPreviewOwner { get; private set; }
    // Retain the last session on clear so the transport can reject a delayed update.
    public Guid PreviewSessionId { get; private set; }
    // Skin edits can be retired without canceling independent opacity work.
    public Guid SkinEditSessionId { get; private set; }
    public long PreviewEpoch => Volatile.Read(ref _previewEpoch);

    public AppearanceSettingsViewModel(IAppearanceSettingsService store, AppearanceSettings initial,
        Action<AppearanceSettings>? apply = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _apply = apply;
        Current = initial ?? throw new ArgumentNullException(nameof(initial));
        Committed = initial;
    }

    public AppearanceSettings Current { get; private set; }
    // Editor observers must not adopt an optimistic value while a disk save is pending.
    public AppearanceSettings Committed { get; private set; }
    public SkinPackage CommittedSkin => SkinPackage.FromAppearance(Committed);
    // An ordinary choice is already a save intent, not a cancelable editor draft.
    public SkinPackage? PendingSkinSelection => _pendingSkinSelection;
    public long SkinSelectionRevision => _skinSelectionRevision;
    public bool IsSavingSkinSelection => _pendingSkinSelection is not null;
    public bool IsSavingSkin => IsSavingSkinSelection || _pendingSkinCommits != 0;
    public bool? LastSkinSaveSucceeded { get; private set; }
    public bool HasAppearancePreview => CurrentPreviewOwner is not null || IsSavingSkinSelection;
    public bool HasPendingCommits => _pendingCommits != 0 || _pendingSelectionOperations != 0;
    public Task WhenIdleAsync() => _commitsIdle?.Task ?? Task.CompletedTask;
    public AppearanceSettings? CurrentPreview => HasSkinPreview || IsSavingSkinSelection || _previewTransparency is not null
        ? Current with { Skin = _pendingSkinSelection ?? _previewSkin ?? Current.Skin,
            TransparencyPercent = _previewTransparency ?? Current.TransparencyPercent }
        : null;
    public AppearanceSettings Effective => CurrentPreview ?? Current;
    public bool HasSkinPreview => _previewSkin is not null;
    public bool CanRevertSkin => _previousSkin is not null;
    public string? ErrorText { get; private set; }
    public event Action? Changed;
    // Transient snapshots never enter appearance.json. Null clears host preview.
    public event Action<AppearanceSettings?>? PreviewChanged;

    public bool TryBeginPreview(Guid owner)
    {
        if (CurrentPreviewOwner is { } active) return active == owner;
        CurrentPreviewOwner = owner;
        PreviewSessionId = Guid.NewGuid();
        SkinEditSessionId = PreviewSessionId;
        _pendingPreviewCommits = 0;
        _pendingSkinDraft = false;
        AdvancePreviewEpoch();
        NotifyPreview();
        NotifyChanged();
        return true;
    }

    public bool IsPreviewOwner(Guid owner, Guid sessionId) => CurrentPreviewOwner == owner && PreviewSessionId == sessionId;
    public bool IsSkinPreviewOwner(Guid owner, Guid sessionId) => CurrentPreviewOwner == owner && SkinEditSessionId == sessionId;

    // Reserve a debounced skin edit before rendering it. An unrelated setting or
    // opacity save must not release this owner while its draft is still queued.
    public bool TryReserveSkinPreview(Guid owner, Guid sessionId)
    {
        if (!IsSkinPreviewOwner(owner, sessionId) || IsSavingSkinSelection) return false;
        _pendingSkinDraft = true;
        return true;
    }

    public void BeginPreview() => PreviewSkin(_previewSkin ?? SkinPackage.FromAppearance(Current));

    public void PreviewSkin(SkinPackage skin)
    {
        if (!TryBeginPreview(LegacyPreviewOwner) || !TryPreviewSkin(LegacyPreviewOwner, SkinEditSessionId, skin))
            throw new InvalidOperationException("Another appearance window owns the preview.");
    }

    public bool TryPreviewSkin(Guid owner, SkinPackage skin) => TryBeginPreview(owner)
        && TryPreviewSkin(owner, SkinEditSessionId, skin);

    public bool TryPreviewSkin(Guid owner, Guid sessionId, SkinPackage skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (!IsSkinPreviewOwner(owner, sessionId) || IsSavingSkinSelection) return false;
        var previous = _previewSkin;
        _pendingSkinDraft = false;
        _previewSkin = skin;
        try { _apply?.Invoke(Effective); }
        catch
        {
            _previewSkin = previous;
            TryReleaseIdlePreview(owner, PreviewSessionId);
            try { _apply?.Invoke(Effective); NotifyPreview(); }
            catch (Exception) { }
            NotifyChanged();
            throw;
        }
        _previewRevision++;
        AdvancePreviewEpoch();
        ErrorText = null;
        NotifyPreview();
        NotifyChanged();
        return true;
    }

    public void CancelPreview() => TryCancelPreview(LegacyPreviewOwner);

    public bool TryCancelPreview(Guid owner) => TryCancelPreview(owner, PreviewSessionId);

    public bool TryCancelPreview(Guid owner, Guid sessionId)
    {
        if (!IsPreviewOwner(owner, sessionId)) return false;
        _previewSkin = null;
        _previewTransparency = null;
        _pendingSkinDraft = false;
        CurrentPreviewOwner = null;
        _previewRevision++;
        AdvancePreviewEpoch();
        try { _apply?.Invoke(Effective); }
        finally { NotifyPreview(); NotifyChanged(); }
        return true;
    }

    public Task ApplySkinAsync(SkinPackage? skin = null, CancellationToken cancellationToken = default)
    {
        if (skin is null && _previewSkin is null) return Task.CompletedTask;
        if (!TryBeginPreview(LegacyPreviewOwner)) return Task.CompletedTask;
        return ApplySkinAsync(LegacyPreviewOwner, SkinEditSessionId, skin, cancellationToken);
    }

    public Task<bool> ApplySkinAsync(Guid owner, SkinPackage? skin = null, CancellationToken cancellationToken = default) =>
        ApplySkinAsync(owner, SkinEditSessionId, skin, cancellationToken);

    public Task<bool> ApplySkinAsync(Guid owner, Guid sessionId, SkinPackage? skin = null, CancellationToken cancellationToken = default)
    {
        if (!IsSkinPreviewOwner(owner, sessionId) || IsSavingSkinSelection) return Task.FromResult(false);
        var selected = skin ?? _previewSkin;
        if (selected is null) return Task.FromResult(false);
        return CommitAsync(current => WithSkin(current, selected), cancellationToken, skinChange: true,
            previewOwner: owner, previewSession: PreviewSessionId, skinEditSession: sessionId);
    }

    public Task<bool> SelectAccentAsync(Guid owner, AccentKind accent, bool dark, CancellationToken cancellationToken = default)
    {
        if (!AccentPalette.Presets.Any(swatch => swatch.Kind == accent && accent != AccentKind.Default))
            return Task.FromResult(false);
        // A named color changes the visible mode of the latest ordinary choice,
        // never implicitly saves an imported or incomplete advanced draft.
        var selected = (_pendingSkinSelection ?? CommittedSkin).WithColor(dark, SkinColorRole.Accent,
            AccentPalette.Resolve(accent, null, dark));
        return SelectSkinAsync(owner, selected, cancellationToken);
    }

    public async Task<bool> SelectSkinAsync(Guid owner, SkinPackage selected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (CurrentPreviewOwner is { } active && active != owner) return false;
        var selectionRevision = ++_skinSelectionRevision;
        _pendingSkinSelection = selected;
        LastSkinSaveSucceeded = null;
        ErrorText = null;
        // Retire only the advanced skin draft. Opacity has its own revision and
        // any queued opacity commit keeps its existing owner/session.
        _previewSkin = null;
        _pendingSkinDraft = false;
        _previewRevision++;
        SkinEditSessionId = Guid.NewGuid();
        if (CurrentPreviewOwner is { } previewOwner)
            TryReleaseIdlePreview(previewOwner, PreviewSessionId);
        if (CurrentPreviewOwner is null) PreviewSessionId = Guid.NewGuid();
        AdvancePreviewEpoch();
        var saved = false;
        BeginPendingWork(selection: true);
        try
        {
            // Show the latest click immediately, including while another write
            // owns the gate. This overlay is never consumed by editor Cancel.
            _apply?.Invoke(Effective);
            NotifyPreview();
            NotifyChanged();
            saved = await CommitAsync(current => WithSkin(current, selected), cancellationToken,
                skinChange: true, skinSelectionRevision: selectionRevision);
            return saved;
        }
        catch (Exception)
        {
            if (selectionRevision == _skinSelectionRevision) ErrorText = StringTable.Get("Error_SaveAppearance");
            return false;
        }
        finally
        {
            // An older completion must not repaint, clear the saving indicator,
            // or publish a transport clear over a newer click.
            if (selectionRevision == _skinSelectionRevision)
            {
                _pendingSkinSelection = null;
                LastSkinSaveSucceeded = saved;
                AdvancePreviewEpoch();
                try { _apply?.Invoke(Effective); }
                catch (Exception) { }
                NotifyPreview();
                NotifyChanged();
            }
            EndPendingWork(selection: true);
        }
    }

    private static AppearanceSettings WithSkin(AppearanceSettings current, SkinPackage selected)
    {
        // The paired skin is authoritative. Keep legacy accent fields synchronized
        // with its light accent in the same commit for older profile readers.
        var named = AccentPalette.Presets.FirstOrDefault(s => AccentPalette.Resolve(s.Kind, null, false) == selected.Light.Accent);
        return current with { Skin = selected,
            Accent = named.NameKey is null ? AccentKind.Custom : named.Kind,
            CustomAccent = named.NameKey is null ? AccentPalette.ToHex(selected.Light.Accent) : current.CustomAccent };
    }

    public Task RevertSkinAsync(CancellationToken cancellationToken = default)
    {
        if (_previousSkin is null) return Task.CompletedTask;
        if (!TryBeginPreview(LegacyPreviewOwner)) return Task.CompletedTask;
        return RevertSkinAsync(LegacyPreviewOwner, SkinEditSessionId, cancellationToken);
    }

    public Task<bool> RevertSkinAsync(Guid owner, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!IsSkinPreviewOwner(owner, sessionId) || IsSavingSkinSelection) return Task.FromResult(false);
        return CommitAsync(current => _previousSkin is { } previous
            ? current with { Skin = previous.Package, Accent = previous.Accent, CustomAccent = previous.CustomAccent }
            : current, cancellationToken, skinChange: true, reverting: true,
            previewOwner: owner, previewSession: PreviewSessionId, skinEditSession: sessionId);
    }

    public Task SetThemeAndAccentAsync(AppThemeKind theme, AccentKind accent, CancellationToken cancellationToken = default) =>
        CommitAsync(current => WithAccent(current with { Theme = theme }, accent, current.CustomAccent), cancellationToken);
    public Task SetThemeAsync(AppThemeKind theme, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { Theme = theme }, cancellationToken);
    public Task SetBackdropAsync(BackdropKind backdrop, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { Backdrop = backdrop }, cancellationToken);
    public Task SetFileTypographyAsync(string? family, double nameSize, double detailsSize, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { FileFontFamily = AppearanceSettings.SanitizeFont(family),
            FileNameFontSize = AppearanceSettings.SanitizeFontSize(nameSize, 13, 24),
            FileDetailsFontSize = AppearanceSettings.SanitizeFontSize(detailsSize, 12, 20) }, cancellationToken);
    public Task SetShowStatusBarAsync(bool show, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { ShowStatusBar = show }, cancellationToken);
    public Task SetShowToolbarAsync(bool show, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { ShowToolbar = show }, cancellationToken);
    public Task SetUseBundledFileIconsAsync(bool useBundled, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { UseBundledFileIcons = useBundled }, cancellationToken);
    public Task SetReduceMotionAsync(ReduceMotionKind reduce, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { ReduceMotion = reduce }, cancellationToken);
    public Task SetGlassEffectAsync(GlassEffectMode glassEffect, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { GlassEffect = glassEffect,
            TransparencyPercent = AppearanceSettings.PresetTransparencyPercent(glassEffect) }, cancellationToken,
            clearTransparency: true);
    public Task<bool> SetGlassEffectAsync(Guid owner, Guid sessionId, GlassEffectMode glassEffect, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { GlassEffect = glassEffect,
            TransparencyPercent = AppearanceSettings.PresetTransparencyPercent(glassEffect) }, cancellationToken,
            clearTransparency: true, previewOwner: owner, previewSession: sessionId);

    public void PreviewTransparencyPercent(int percent)
    {
        if (TryBeginPreview(LegacyPreviewOwner)) TryPreviewTransparencyPercent(LegacyPreviewOwner, PreviewSessionId, percent);
    }

    public bool TryPreviewTransparencyPercent(Guid owner, int percent) => TryBeginPreview(owner)
        && TryPreviewTransparencyPercent(owner, PreviewSessionId, percent);

    public bool TryPreviewTransparencyPercent(Guid owner, Guid sessionId, int percent)
    {
        if (!IsPreviewOwner(owner, sessionId)) return false;
        var previous = _previewTransparency;
        _previewTransparency = Math.Clamp(percent, 0, 100);
        try { _apply?.Invoke(Effective); }
        catch
        {
            _previewTransparency = previous;
            if (!TryReleaseIdlePreview(owner, sessionId)) AdvancePreviewEpoch();
            try { _apply?.Invoke(Effective); NotifyPreview(); }
            catch (Exception) { }
            NotifyChanged();
            throw;
        }
        _transparencyRevision++;
        AdvancePreviewEpoch();
        ErrorText = null;
        NotifyPreview();
        return true;
    }

    public Task SetTransparencyPercentAsync(int percent, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { TransparencyPercent = Math.Clamp(percent, 0, 100) }, cancellationToken,
            clearTransparency: true);
    public Task<bool> SetTransparencyPercentAsync(Guid owner, Guid sessionId, int percent, CancellationToken cancellationToken = default) =>
        CommitAsync(current => current with { TransparencyPercent = Math.Clamp(percent, 0, 100) }, cancellationToken,
            clearTransparency: true, previewOwner: owner, previewSession: sessionId);
    public Task SetAccentAsync(AccentKind accent, CancellationToken cancellationToken = default) =>
        CommitAsync(current => WithAccent(current, accent, current.CustomAccent), cancellationToken);
    public Task SetCustomAccentAsync(string? customAccent, CancellationToken cancellationToken = default)
    {
        if (!AccentPalette.TryParse(customAccent, out var color)) return Task.CompletedTask;
        return CommitAsync(current => WithAccent(current, AccentKind.Custom, AccentPalette.ToHex(color)), cancellationToken);
    }

    private static AppearanceSettings WithAccent(AppearanceSettings current, AccentKind accent, string? customAccent)
    {
        var skin = current.Skin;
        if (skin is not null)
            skin = skin.WithColor(false, SkinColorRole.Accent, AccentPalette.Resolve(accent, customAccent, false) | 0xFF000000u)
                .WithColor(true, SkinColorRole.Accent, AccentPalette.Resolve(accent, customAccent, true) | 0xFF000000u);
        return current with { Accent = accent, CustomAccent = customAccent, Skin = skin };
    }

    private async Task<bool> CommitAsync(Func<AppearanceSettings, AppearanceSettings> update, CancellationToken cancellationToken,
        bool skinChange = false, bool reverting = false, bool clearTransparency = false,
        Guid? previewOwner = null, Guid? previewSession = null, long? skinSelectionRevision = null, Guid? skinEditSession = null)
    {
        // Capture edit intent before waiting. A newer draft may arrive while an
        // earlier save holds the gate and must remain visible after this commit.
        var revision = _previewRevision;
        var selectionAtRequest = _skinSelectionRevision;
        var transparencyRevision = _transparencyRevision;
        var registered = false;
        var entered = false;
        if (previewOwner is { } owner && previewSession is { } session)
        {
            if (!IsPreviewOwner(owner, session)) return false;
            _pendingPreviewCommits++;
            registered = true;
        }
        BeginPendingWork(selection: false);
        if (skinChange)
        {
            _pendingSkinCommits++;
            if (skinSelectionRevision is null || skinSelectionRevision == _skinSelectionRevision)
                LastSkinSaveSucceeded = null;
        }
        void Unregister()
        {
            if (!registered) return;
            registered = false;
            if (CurrentPreviewOwner == previewOwner && PreviewSessionId == previewSession) _pendingPreviewCommits--;
        }
        try
        {
            await _commitGate.WaitAsync(cancellationToken);
            entered = true;
            if (previewOwner is { } required && (CurrentPreviewOwner != required || PreviewSessionId != previewSession)) return false;
            if (skinEditSession is { } requiredSkinSession && SkinEditSessionId != requiredSkinSession) return false;
            if (skinSelectionRevision is { } requestedSelection && requestedSelection != _skinSelectionRevision) return false;
            if (skinChange && skinSelectionRevision is null && selectionAtRequest != _skinSelectionRevision) return false;
            if (reverting && _previousSkin is null) return false;
            var previous = Current;
            // Retain ordinary desired changes across an older failed write when a
            // newer intent was already queued. Never replay failed private skin or
            // owner-scoped opacity commits into unrelated settings saves.
            var desired = previous;
            foreach (var intent in _uncommittedOrdinaryIntents) desired = intent(desired);
            var next = update(desired);
            PerformanceTrace.Record(PerformanceStage.AppearanceCommitStarted, previewOwner ?? Guid.Empty,
                generation: revision, auxiliary: skinChange ? 1 : 0);
            var commitOwner = CurrentPreviewOwner;
            var commitSession = PreviewSessionId;
            if (clearTransparency && transparencyRevision == _transparencyRevision && (CurrentPreviewOwner is null || CurrentPreviewOwner == LegacyPreviewOwner
                || CurrentPreviewOwner == previewOwner)) _previewTransparency = null;
            Current = next;
            // Every consumer reading Effective during _apply must see this commit,
            // including a direct Apply that differs from the preceding draft.
            if (skinChange && previewOwner is not null && revision == _previewRevision) _previewSkin = next.Skin;
            if (HasAppearancePreview) AdvancePreviewEpoch();
            var succeeded = false;
            try
            {
                _apply?.Invoke(Effective);
                NotifyPreview();
                await _store.SaveAsync(next, cancellationToken); // Retain the caller's UI synchronization context.
                succeeded = true;
                Committed = next;
                _uncommittedOrdinaryIntents.Clear();
                if (!skinChange || selectionAtRequest == _skinSelectionRevision) ErrorText = null;
                if (skinChange)
                {
                    _previousSkin = new SkinHistory(previous.Skin, previous.Accent, previous.CustomAccent);
                    if (revision == _previewRevision) _previewSkin = null;
                }
            }
            catch (Exception error)
            {
                Current = previous;
                if (!skinChange && previewOwner is null && error is not OperationCanceledException)
                    _uncommittedOrdinaryIntents.Add(update);
                // A failed Apply restores committed runtime; a newer in-flight draft survives.
                if (skinChange && revision == _previewRevision) _previewSkin = null;
                try { _apply?.Invoke(Effective); NotifyPreview(); }
                catch (Exception) { }
                if (!skinChange || selectionAtRequest == _skinSelectionRevision)
                    ErrorText = StringTable.Get("Error_SaveAppearance");
            }
            if (skinChange && selectionAtRequest == _skinSelectionRevision)
                LastSkinSaveSucceeded = succeeded;
            Unregister();
            // Only a commit explicitly consuming this preview may end it. Generic
            // settings commits leave an acquired/debounced skin session intact.
            if (previewOwner is not null || (clearTransparency && commitOwner == LegacyPreviewOwner))
                TryReleaseIdlePreview(commitOwner, commitSession);
            // Rebind auxiliary windows after the transient snapshot is cleared.
            // An observer failure after Save cannot roll back an already committed file.
            if (skinChange)
            {
                try { _apply?.Invoke(Effective); }
                catch (Exception) { }
            }
            NotifyPreview();
            NotifyChanged();
            PerformanceTrace.Record(PerformanceStage.AppearanceCommitCompleted, previewOwner ?? Guid.Empty,
                generation: revision, outcome: succeeded ? PerformanceOutcome.Completed : PerformanceOutcome.Failed,
                auxiliary: skinChange ? 1 : 0);
            return succeeded;
        }
        finally
        {
            try
            {
                if (registered)
                {
                    Unregister();
                    // A canceled gate wait can be the last queued operation. Do not
                    // leave an empty session locked, or disturb a replacement session.
                    if (TryReleaseIdlePreview(previewOwner, previewSession))
                    { NotifyPreview(); NotifyChanged(); }
                }
            }
            finally
            {
                if (skinChange) _pendingSkinCommits--;
                if (entered) _commitGate.Release();
                EndPendingWork(selection: false);
            }
        }
    }

    private void BeginPendingWork(bool selection)
    {
        if (!HasPendingCommits)
            _commitsIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (selection) _pendingSelectionOperations++;
        else _pendingCommits++;
    }

    private void EndPendingWork(bool selection)
    {
        if (selection) _pendingSelectionOperations--;
        else if (--_pendingCommits == 0) _uncommittedOrdinaryIntents.Clear();
        if (!HasPendingCommits) _commitsIdle?.TrySetResult();
    }

    private bool TryReleaseIdlePreview(Guid? owner, Guid? session)
    {
        if (owner is null || CurrentPreviewOwner != owner || PreviewSessionId != session
            || _pendingPreviewCommits != 0 || _pendingSkinDraft || _previewSkin is not null || _previewTransparency is not null)
            return false;
        CurrentPreviewOwner = null;
        AdvancePreviewEpoch();
        return true;
    }

    private void AdvancePreviewEpoch() => Interlocked.Increment(ref _previewEpoch);

    private void NotifyChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* UI observers cannot turn a completed disk save into failure. */ }
    }

    private void NotifyPreview()
    {
        try { PreviewChanged?.Invoke(CurrentPreview); }
        catch (Exception) { /* A transient preview transport failure must not undo a disk commit. */ }
    }

    private sealed record SkinHistory(SkinPackage? Package, AccentKind Accent, string? CustomAccent);
}
