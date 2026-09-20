namespace FilesMate.App.Navigation;

/// <summary>Explorer-style incremental selection without a second directory scan.</summary>
public sealed class NameJumpSession
{
    private string _prefix = string.Empty;
    private TimeSpan _lastInput;
    private static readonly TimeSpan ResetAfter = TimeSpan.FromSeconds(1);

    public int Find(char character, TimeSpan now, int current, int count, Func<int, string> nameAt)
    {
        if (char.IsControl(character) || count == 0) return -1;
        var continuing = _prefix.Length > 0 && now - _lastInput < ResetAfter;
        var cycling = continuing && _prefix.Length == 1
            && string.Equals(_prefix, character.ToString(), StringComparison.OrdinalIgnoreCase);
        _prefix = continuing && !cycling ? _prefix + character : character.ToString();
        _lastInput = now;
        if (_prefix.Length > 128) _prefix = character.ToString();
        // A longer prefix can refine the current match. A single/repeated letter
        // starts after the current selection and wraps once through the view.
        var start = continuing && !cycling ? Math.Max(0, current) : current + 1;
        for (var offset = 0; offset < count; offset++)
        {
            var index = (Math.Max(0, start) + offset) % count;
            if (nameAt(index).StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }

    public void Reset() => _prefix = string.Empty;
}
