using System.Text;

namespace FilesMate.Core.Entries;

/// <summary>Offline, dictionary-primary readings. Never calls the filesystem or a network service.</summary>
public static class PinyinName
{
    private sealed record Table(int[] Codepoints, ushort[] Readings, string[] Syllables);
    private static readonly Lazy<Table> Data = new(ReadTable);

    public static bool StartsWithHan(string name) => name.Length > 0 && Rune.TryGetRuneAt(name, 0, out var rune) && IsHan(rune.Value);

    private static bool IsHan(int value) => value is >= 0x3400 and <= 0x9fff or >= 0xf900 and <= 0xfaff or >= 0x20000 and <= 0x323af;

    public static string Key(string name)
    {
        var hasHan = false;
        foreach (var rune in name.EnumerateRunes())
            if (IsHan(rune.Value)) { hasHan = true; break; }
        if (!hasHan) return name;
        var table = Data.Value;
        var text = new StringBuilder(name.Length * 2);
        foreach (var rune in name.EnumerateRunes())
        {
            var index = IsHan(rune.Value) ? Array.BinarySearch(table.Codepoints, rune.Value) : -1;
            if (index >= 0) text.Append(table.Syllables[table.Readings[index]]);
            else text.Append(rune.ToString());
        }
        return text.ToString();
    }

    public static string Initial(string key)
    {
        if (key.Length == 0) return "#";
        var first = char.ToUpperInvariant(key[0]);
        if (first is >= 'A' and <= 'Z') return first.ToString();
        return char.IsLetter(first) || char.IsSurrogate(first) ? "其他" : "#";
    }

    private static Table ReadTable()
    {
        using var stream = typeof(PinyinName).Assembly.GetManifestResourceStream("FilesMate.Mandarin17")
            ?? throw new InvalidOperationException("Missing offline Mandarin table.");
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "FMP1") throw new InvalidDataException("Invalid Mandarin table.");
        var syllables = new string[reader.ReadUInt16()];
        var count = reader.ReadInt32();
        for (var i = 0; i < syllables.Length; i++) syllables[i] = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadByte()));
        var points = new int[count];
        var readings = new ushort[count];
        for (var i = 0; i < count; i++) { points[i] = reader.ReadInt32(); readings[i] = reader.ReadUInt16(); }
        return new(points, readings, syllables);
    }
}
