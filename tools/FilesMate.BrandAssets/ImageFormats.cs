using System.IO.Compression;
using System.Text;

namespace FilesMate.BrandAssets;

internal static class ImageFormats
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();

    internal static byte[] WritePng(int width, int height, byte[] rgba)
    {
        using var stream = new MemoryStream();
        stream.Write(PngSignature);

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(stream, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba, y * stride, stride);
            }
        }

        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    internal static byte[] WriteIco(IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        foreach (var frame in frames)
        {
            writer.Write(frame.Size >= 256 ? (byte)0 : (byte)frame.Size);
            writer.Write(frame.Size >= 256 ? (byte)0 : (byte)frame.Size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frame.Png.Length);
            writer.Write(offset);
            offset += frame.Png.Length;
        }

        foreach (var frame in frames)
        {
            writer.Write(frame.Png);
        }

        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var crcBuffer = new byte[typeBytes.Length + payload.Length];
        typeBytes.CopyTo(crcBuffer, 0);
        payload.CopyTo(crcBuffer, typeBytes.Length);

        WriteBigEndianToStream(stream, payload.Length);
        stream.Write(typeBytes);
        stream.Write(payload);
        WriteBigEndianToStream(stream, (int)Crc(crcBuffer));
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteBigEndianToStream(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
