namespace FilesMate.Search;

/// <summary>UI-confined sender state. Coalescing never loses terminal session identity or sequence.</summary>
public sealed class AppearancePreviewRelay(AppearancePreviewOwner owner)
{
    private Guid _sourceSession;
    private Guid _wireSession;
    private long _epoch;

    public SearchHostRequest? Current { get; private set; }
    public bool NeedsHeartbeat => Current is { PreviewOperation: not AppearancePreviewProtocol.Clear };

    public SearchHostRequest Publish(Guid sourceSession, bool ownsPreview, SearchAppearancePreview? preview)
    {
        if (sourceSession == Guid.Empty) throw new ArgumentException("A preview session is required.", nameof(sourceSession));
        if (!ownsPreview && preview is not null) throw new ArgumentException("A draft requires preview ownership.", nameof(preview));
        if (_sourceSession != sourceSession)
        {
            _sourceSession = sourceSession;
            _wireSession = sourceSession;
        }
        return Current = new("appearance-preview", AppearancePreview: preview, PreviewOwnerPid: owner.Pid,
            PreviewSessionId: _wireSession.ToString("N"), PreviewEpoch: checked(++_epoch),
            PreviewOwnerStartedUtcTicks: owner.StartedUtcTicks,
            PreviewOperation: !ownsPreview ? AppearancePreviewProtocol.Clear
                : preview is null ? AppearancePreviewProtocol.Acquire : AppearancePreviewProtocol.Update);
    }

    public bool RenewExpired(SearchHostRequest sent)
    {
        // A delayed reply cannot revive a canceled draft or supersede a newer edit.
        if (!ReferenceEquals(sent, Current) || !NeedsHeartbeat) return false;
        _wireSession = Guid.NewGuid();
        Current = sent with { PreviewSessionId = _wireSession.ToString("N"), PreviewEpoch = checked(++_epoch) };
        return true;
    }

    public SearchHostRequest? Clear()
    {
        if (Current is not { } last) return null;
        return Current = last with { AppearancePreview = null, PreviewOperation = AppearancePreviewProtocol.Clear,
            PreviewEpoch = checked(++_epoch) };
    }
}
