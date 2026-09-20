namespace FilesMate.Core.Icons;

/// <summary>
/// Premultiplied BGRA pixels from a converted HICON. The platform releases native handles before returning this.
/// </summary>
public sealed class IconBitmap
{
    public IconBitmap(int width, int height, byte[] bgra)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bgra);
        var expected = checked(width * height * 4);
        if (bgra.Length != expected)
        {
            throw new ArgumentException($"BGRA buffer must be {expected} bytes.", nameof(bgra));
        }

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra { get; }
}
