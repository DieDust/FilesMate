namespace FilesMate.Core.Presentation;

public readonly record struct PopupBounds(double Left, double Top, double Width, double Height);

/// <summary>Pointer starts the top-left corner; translate only as far as needed to fit the invoking work area.</summary>
public static class PopupPlacement
{
    public static PopupBounds Clamp(double pointerX, double pointerY, double width, double height, PopupBounds workArea)
    {
        if (!double.IsFinite(pointerX) || !double.IsFinite(pointerY) || !double.IsFinite(width) || !double.IsFinite(height)
            || !double.IsFinite(workArea.Left) || !double.IsFinite(workArea.Top) || !double.IsFinite(workArea.Width)
            || !double.IsFinite(workArea.Height) || width < 0 || height < 0 || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        return new(Math.Clamp(pointerX, workArea.Left, workArea.Left + workArea.Width - width),
            Math.Clamp(pointerY, workArea.Top, workArea.Top + workArea.Height - height), width, height);
    }
}
