using System.Buffers.Binary;

namespace FilesMate.Platform.Windows.Archives;

// Limit metadata before ZipArchive.Entries eagerly allocates central-directory objects.
internal static class ZipDirectoryPreflight
{
    internal static int Validate(FileStream stream, CancellationToken token)
    {
        if (stream.Length < 22) throw Invalid();
        var tail = new byte[(int)Math.Min(stream.Length, 65557)];
        stream.Position = stream.Length - tail.Length;
        stream.ReadExactly(tail);
        var position = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
            if (U32(tail, i) == 0x06054b50)
            {
                // Match the runtime's choice of the last signature. Never validate an
                // earlier header while ZipArchive would consume a forged one in its comment.
                if (i + 22 + U16(tail, i + 20) != tail.Length) throw Invalid();
                position = i;
                break;
            }
        if (position < 0 || U16(tail, position + 4) != 0 || U16(tail, position + 6) != 0) throw Invalid();
        ulong count = U16(tail, position + 10), size = U32(tail, position + 12), offset = U32(tail, position + 16);
        if (U16(tail, position + 8) != count) throw Invalid();
        var endPosition = stream.Length - tail.Length + position;
        if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
        {
            // .NET 10 chooses ZIP64 from count/offset, not the size sentinel alone.
            if (count != ushort.MaxValue && offset != uint.MaxValue) throw Invalid();
            if (endPosition < 76) throw Invalid();
            var locator = new byte[20];
            stream.Position = endPosition - 20;
            stream.ReadExactly(locator);
            if (U32(locator, 0) != 0x07064b50 || U32(locator, 4) != 0 || U32(locator, 16) != 1) throw Invalid();
            var zip64Position = U64(locator, 8);
            if (zip64Position > (ulong)(endPosition - 20 - 56)) throw Invalid();
            stream.Position = (long)zip64Position;
            var header = new byte[56];
            stream.ReadExactly(header);
            if (U32(header, 0) != 0x06064b50 || U64(header, 4) < 44 || U32(header, 16) != 0 || U32(header, 20) != 0 ||
                U64(header, 24) != U64(header, 32) || U64(header, 4) != (ulong)(endPosition - 20) - zip64Position - 12) throw Invalid();
            count = U64(header, 32); size = U64(header, 40); offset = U64(header, 48);
            endPosition = (long)zip64Position;
        }
        if (count > ZipArchiveService.MaxEntries) throw ArchivePathGuard.Error(ArchiveErrorCode.TooManyEntries);
        if (size > ZipArchiveService.MaxMetadataBytes) throw ArchivePathGuard.Error(ArchiveErrorCode.MetadataTooLarge);
        // Reject gaps as well as overflow: .NET may keep parsing central headers beyond
        // a forged declared size. The validated directory must end at its real trailer.
        if (offset > (ulong)endPosition || size != (ulong)endPosition - offset || count * 46 > size) throw Invalid();

        // Do not trust a forged EOCD entry count: walk actual records inside the bounded region.
        stream.Position = (long)offset;
        var centralEnd = (long)(offset + size);
        var fixedHeader = new byte[46];
        var actual = 0;
        while (stream.Position < centralEnd)
        {
            token.ThrowIfCancellationRequested();
            if (++actual > ZipArchiveService.MaxEntries) throw ArchivePathGuard.Error(ArchiveErrorCode.TooManyEntries);
            if (centralEnd - stream.Position < 46) throw Invalid();
            stream.ReadExactly(fixedHeader);
            if (U32(fixedHeader, 0) != 0x02014b50) throw Invalid();
            var flags = U16(fixedHeader, 8);
            var method = U16(fixedHeader, 10);
            if ((flags & (1 | 0x40 | 0x2000)) != 0 || method is not (0 or 8))
                throw ArchivePathGuard.Error(ArchiveErrorCode.UnsupportedEntry);
            if (U16(fixedHeader, 34) != 0) throw Invalid();
            var variableLength = (long)U16(fixedHeader, 28) + U16(fixedHeader, 30) + U16(fixedHeader, 32);
            if (variableLength > centralEnd - stream.Position) throw Invalid();
            stream.Position += variableLength;
        }
        if ((ulong)actual != count) throw Invalid();
        stream.Position = 0;
        return actual;
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
    private static ArchiveOperationException Invalid() => ArchivePathGuard.Error(ArchiveErrorCode.InvalidArchive);
}
