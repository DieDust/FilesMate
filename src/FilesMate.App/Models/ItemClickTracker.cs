namespace FilesMate.App.Models;

/// <summary>Recognizes completed clicks, including the second half of a double-click after navigation.</summary>
public sealed class ItemClickTracker
{
    private Click? _previous;
    private Click? _opened;

    public void CancelPendingClick() => _previous = null;

    public bool ShouldOpen(string identity, ItemOpeningMode mode, bool onName, long milliseconds, double x, double y,
        int doubleClickMilliseconds, double toleranceX, double toleranceY)
    {
        if (mode == ItemOpeningMode.NameClick && !onName)
        {
            CancelPendingClick();
            return false;
        }
        var click = new Click(identity, milliseconds, x, y);
        if (_opened is { } opened && CloseTo(opened))
        {
            // The first click may already have changed the folder under the pointer.
            // Suppress the second click even if it now hits a different entry.
            _previous = null;
            return false;
        }
        var invoke = mode != ItemOpeningMode.DoubleClick || (_previous is { } previous
            && previous.Identity == identity && CloseTo(previous));
        _previous = invoke ? null : click;
        if (invoke) _opened = click;
        return invoke;

        bool CloseTo(Click earlier) => milliseconds >= earlier.Milliseconds
            && milliseconds - earlier.Milliseconds <= doubleClickMilliseconds
            && Math.Abs(x - earlier.X) <= toleranceX && Math.Abs(y - earlier.Y) <= toleranceY;
    }

    private readonly record struct Click(string Identity, long Milliseconds, double X, double Y);
}
