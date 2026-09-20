namespace FilesMate.Core.Entries;

/// <summary>
/// Case-insensitive natural comparison (digits as numbers). Windows production code should use
/// <c>WindowsNameComparer</c> (<c>StrCmpLogicalW</c>) instead of this portable stand-in.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var cmp = CompareNumbers(x, ref i, y, ref j);
                if (cmp != 0)
                {
                    return cmp;
                }

                continue;
            }

            var cx = char.ToUpperInvariant(x[i]);
            var cy = char.ToUpperInvariant(y[j]);
            if (cx != cy)
            {
                return cx.CompareTo(cy);
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int CompareNumbers(string x, ref int i, string y, ref int j)
    {
        while (i < x.Length && x[i] == '0')
        {
            i++;
        }

        while (j < y.Length && y[j] == '0')
        {
            j++;
        }

        var startX = i;
        var startY = j;
        while (i < x.Length && char.IsDigit(x[i]))
        {
            i++;
        }

        while (j < y.Length && char.IsDigit(y[j]))
        {
            j++;
        }

        var lenX = i - startX;
        var lenY = j - startY;
        if (lenX != lenY)
        {
            return lenX.CompareTo(lenY);
        }

        for (var k = 0; k < lenX; k++)
        {
            var cmp = x[startX + k].CompareTo(y[startY + k]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }
}
