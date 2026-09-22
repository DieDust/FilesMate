using System.Text;

namespace FilesMate.Platform.Windows.Archives;

internal static class ArchiveSecurityZone
{
    private const int MaxMetadataLength = 16 * 1024;

    internal static int? Read(string archivePath)
    {
        try
        {
            using var input = new FileStream(archivePath + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaxMetadataLength) throw Failure();
            using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var inSection = false;
            int? zone = null;
            while (reader.ReadLine() is { } raw)
            {
                var line = raw.Trim();
                if (line.StartsWith(';') || line.StartsWith('#') || line.Length == 0) continue;
                if (line.StartsWith('[')) { inSection = line.Equals("[ZoneTransfer]", StringComparison.OrdinalIgnoreCase); continue; }
                var split = line.IndexOf('=');
                if (!inSection || split < 0 || !line[..split].Trim().Equals("ZoneId", StringComparison.OrdinalIgnoreCase)) continue;
                if (zone.HasValue || !int.TryParse(line[(split + 1)..].Trim(), out var value) || value is < 0 or > 4) throw Failure();
                zone = value;
            }
            if (!zone.HasValue) throw Failure();
            return zone >= 3 ? zone : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Failure(); }
    }

    internal static void Write(string outputPath, int? zone)
    {
        if (!zone.HasValue) return;
        try
        {
            // The main data stream is still exclusively owned by CreateNew. Named
            // streams need delete sharing to coexist with its retained DELETE handle.
            using var output = new FileStream(outputPath + ":Zone.Identifier", FileMode.CreateNew,
                FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            output.Write(Encoding.ASCII.GetBytes($"[ZoneTransfer]\r\nZoneId={zone.Value}\r\n"));
            output.Flush();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { throw Failure(); }
    }

    private static ArchiveOperationException Failure() => ArchivePathGuard.Error(ArchiveErrorCode.SecurityMetadataUnavailable);
}
