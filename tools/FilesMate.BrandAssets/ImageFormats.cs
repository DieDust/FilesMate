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

    internal static byte[] WriteIco(IReadOnlyList<(int Size, byte[] Rgba)> frames)
    {
        // Small window/shell icons also pass through legacy GDI image-and-mask
        // drawing. Give them an explicit AND mask; retain PNG for the large frame.
        var images = frames.Select(frame => frame.Size < 256
            ? WriteIconBitmap(frame.Size, frame.Rgba)
            : WritePng(frame.Size, frame.Size, frame.Rgba)).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            writer.Write(frame.Size >= 256 ? (byte)0 : (byte)frame.Size);
            writer.Write(frame.Size >= 256 ? (byte)0 : (byte)frame.Size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }

        return stream.ToArray();
    }

    private static byte[] WriteIconBitmap(int size, byte[] rgba)
    {
        var maskStride = ((size + 31) / 32) * 4;
        var mask = new byte[maskStride * size];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(40); // BITMAPINFOHEADER
        writer.Write(size);
        writer.Write(size * 2); // Color bitmap followed by the monochrome AND mask.
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0); // BI_RGB
        writer.Write(0); // Uncompressed bitmap size may be zero.
        writer.Write(0); // Horizontal resolution.
        writer.Write(0); // Vertical resolution.
        writer.Write(0); // No color table.
        writer.Write(0); // All colors are important.

        // Both bitmaps are bottom-up. BGRA is straight alpha, like the PNG input.
        for (var row = 0; row < size; row++)
        {
            var y = size - 1 - row;
            for (var x = 0; x < size; x++)
            {
                var source = ((y * size) + x) * 4;
                var alpha = rgba[source + 3];
                if (alpha == 0)
                {
                    // Legacy drawing uses (destination AND mask) XOR color.
                    // Transparent pixels must have a white mask AND black RGB,
                    // otherwise invisible yellow RGB in the source tints the background.
                    writer.Write(0);
                    mask[(row * maskStride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
                else
                {
                    writer.Write(rgba[source + 2]);
                    writer.Write(rgba[source + 1]);
                    writer.Write(rgba[source]);
                    writer.Write(alpha);
                }
            }
        }

        writer.Write(mask);
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
