namespace FilesMate.App.Controls.FileSurface;

/// <summary>Accumulates wheel notches and keeps pending column targets independent of rendering.</summary>
public sealed class ListScrollState
{
    private CompactListGeometry? _geometry;
    private long _delta;
    public bool HasTarget { get; private set; }
    public double Target { get; private set; }

    public bool AddWheel(int delta, bool horizontal, CompactListGeometry geometry, double current, double maximum)
    {
        var input = horizontal ? (long)delta : -(long)delta;
        if (input == 0 || geometry.ColumnCount == 0) return false;
        if (!ReferenceEquals(_geometry, geometry)) Reset();
        _geometry = geometry;
        if (_delta != 0 && Math.Sign(_delta) != Math.Sign(input)) _delta = 0;
        _delta += input;
        var steps = _delta / 120;
        _delta %= 120;
        if (steps == 0) return false;
        Target = geometry.WheelOffset(HasTarget ? Target : current, steps, maximum);
        HasTarget = Math.Abs(Target - current) >= .5;
        return true;
    }

    public void ObserveOffset(double current)
    {
        if (HasTarget && Math.Abs(current - Target) < .5) HasTarget = false;
    }

    public void Reset()
    {
        HasTarget = false;
        _geometry = null;
        _delta = 0;
    }
}
