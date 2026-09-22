using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FilesMate.Search;

/// <summary>Coordinates the file manager and resident search host when they update shared index settings.</summary>
internal static class SearchIndexConfigurationFile
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    internal static JsonObject Read(string path)
    {
        // Readers keep their complete old snapshot while a writer atomically publishes the next one.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonNode.Parse(input, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
            ?? throw new JsonException("Search index settings must be a JSON object.");
    }

    internal static void Update(string path, Action<JsonObject> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        path = Path.GetFullPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // The persistent zero-byte lock file must not be deleted: deleting it while another process
        // waits could let two writers lock different files with the same name.
        using var writeLock = AcquireLock(path + ".lock", cancellationToken);
        JsonObject document;
        try { document = Read(path); }
        catch (FileNotFoundException) { document = new JsonObject(); }
        update(document);
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
                document.WriteTo(writer, JsonOptions);
                writer.Flush();
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // Only this operation's uniquely named staging file is eligible for cleanup.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static bool HasProperty(JsonObject document, string name) =>
        document.Any(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal static JsonNode? GetProperty(JsonObject document, string name) =>
        document.LastOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    internal static void SetProperty(JsonObject document, string name, JsonNode? value)
    {
        foreach (var key in document.Select(property => property.Key)
                     .Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray())
            document.Remove(key);
        document[name] = value;
    }

    private static FileStream AcquireLock(string path, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (elapsed.Elapsed < LockTimeout)
            {
                if (cancellationToken.CanBeCanceled) cancellationToken.WaitHandle.WaitOne(25);
                else Thread.Sleep(25);
            }
        }
    }
}
