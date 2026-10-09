namespace FilesMate.App.Controls.Omnibar;

/// <summary>Invalidates asynchronous clipboard work when its editor/display owner changes.</summary>
internal sealed class AddressInputLifetime
{
    private long _clipboardRequest;
    public long Revision { get; private set; }
    public void Changed() => ++Revision;
    // An explicit Enter wins over an earlier clipboard read even if the draft
    // did not change. Keep its revision so a pasted draft retains its origin.
    public void CancelClipboardReads() => ++_clipboardRequest;
    public ClipboardRead BeginClipboardRead() => new(Revision, ++_clipboardRequest);
    public bool IsCurrent(ClipboardRead read) => read.Revision == Revision && read.Request == _clipboardRequest;
    internal readonly record struct ClipboardRead(long Revision, long Request);
}
