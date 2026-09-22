using Microsoft.Data.Sqlite;

namespace FilesMate.Search;

public sealed record NameHit(string Name, string Path, bool IsDirectory, ApplicationEntry? Application)
{
    public NameHit(string name, string path, bool isDirectory) : this(name, path, isDirectory, null) { }
    public void Deconstruct(out string name, out string path, out bool isDirectory)
    { name = Name; path = Path; isDirectory = IsDirectory; }
}

public sealed class NameSearchDiagnostics
{
    public int EvaluatedNames { get; internal set; }
}

/// <summary>Shared filename semantics for the app and the executable Flow plugin.</summary>
public static class NameIndexReader
{
    public static string[] Terms(string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static bool Matches(string name, IReadOnlyList<string> terms) =>
        terms.Count > 0 && terms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

    public static int Relevance(string name, string query) =>
        name.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 0
        : Path.GetFileNameWithoutExtension(name).Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 1
        : name.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 2 : 3;

    public static IReadOnlyList<NameHit> Search(string database, string query, string? prefix, int limit, CancellationToken token = default,
        Func<string, bool, int>? rank = null, Func<string, bool, bool>? filter = null, int offset = 0, bool rankByPath = false,
        NameSearchDiagnostics? diagnostics = null)
    {
        var terms = Terms(query);
        if (terms.Length == 0 || limit <= 0) return [];
        token.ThrowIfCancellationRequested();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 1,
        }.ToString());
        connection.Open();
        using var interrupt = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
        if (diagnostics is not null) diagnostics.EvaluatedNames = 0;
        connection.CreateFunction<string, bool>("fm_match", name =>
        {
            if (diagnostics is not null) diagnostics.EvaluatedNames++;
            return Matches(name, terms);
        }, isDeterministic: true);
        // Rank before LIMIT so callers can prioritize launchable results without
        // losing them to a large number of matching documents or directories.
        connection.CreateFunction<string, string, int>("fm_rank",
            (name, isDirectory) => rank?.Invoke(rankByPath ? name : Path.GetFileName(name), isDirectory == "1") ?? Relevance(Path.GetFileName(name), query), isDeterministic: true);
        connection.CreateFunction<string, bool>("fm_scope", path => prefix is null || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
        connection.CreateFunction<string, string, bool>("fm_filter", (name, directory) => filter?.Invoke(name, directory == "1") ?? true, isDeterministic: true);
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT CASE WHEN EXISTS(SELECT 1 FROM sqlite_master WHERE name='file_name_gram') THEN 3 WHEN EXISTS(SELECT 1 FROM sqlite_master WHERE name='file_name_substring') THEN 2 ELSE 1 END;";
        var schemaVersion = Convert.ToInt32(schema.ExecuteScalar());
        // Encoded invariant n-grams cover one/two-character and CJK queries as well.
        // The exact ordinal predicate remains authoritative after index narrowing.
        var match = schemaVersion == 3
            ? string.Join(" AND ", terms.SelectMany(QueryGrams).Distinct().Select(t => "\"" + t + "\""))
            : string.Join(" AND ", terms.Where(t => t.EnumerateRunes().Count() >= 3).Select(t => "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        using var command = connection.CreateCommand();
        var useIndex = schemaVersion >= 2 && match.Length > 0;
        var table = schemaVersion == 3 ? "file_name_gram" : "file_name_substring";
        command.CommandText = "SELECT f.name, f.path, f.is_dir FROM file_name f "
            + (useIndex ? $" JOIN {table} s ON s.rowid=f.rowid WHERE {table} MATCH $match AND " : " WHERE ")
            + "fm_scope(f.path) AND fm_match(f.name) AND fm_filter(f.path,f.is_dir) "
            + "ORDER BY fm_rank(f.path, f.is_dir), length(f.name), f.name COLLATE NOCASE, f.path LIMIT $limit OFFSET $offset;";
        if (useIndex) command.Parameters.AddWithValue("$match", match);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        var hits = new List<NameHit>();
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                hits.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2) == "1"));
            }
        }
        catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        return hits;
    }

    public static void BuildSubstringIndex(SqliteConnection connection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var interrupt = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
        connection.CreateFunction<string, string>("fm_grams", IndexedGrams, isDeterministic: true);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS file_name_gram
                USING fts5(grams, content='', tokenize='ascii', detail='none', columnsize=0);
            INSERT INTO file_name_gram(file_name_gram) VALUES ('delete-all');
            INSERT INTO file_name_gram(rowid,grams) SELECT rowid,fm_grams(name) FROM file_name;
            INSERT INTO index_meta(key,value) VALUES ('schema','3') ON CONFLICT(key) DO UPDATE SET value='3';
            """;
        try { command.ExecuteNonQuery(); }
        catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
    }

    private static IEnumerable<string> Grams(string folded, int length)
    {
        for (var i = 0; i + length <= folded.Length; i++)
        {
            // Encode directly into the final string. Per-character formatting and a
            // StringBuilder for every gram create gigabytes of temporary rebuild data.
            yield return string.Create(1 + length * 4, (folded, i, length), static (output, state) =>
            {
                const string hex = "0123456789ABCDEF";
                output[0] = 'x';
                for (var j = 0; j < state.length; j++)
                {
                    var value = state.folded[state.i + j];
                    var start = 1 + j * 4;
                    output[start] = hex[value >> 12];
                    output[start + 1] = hex[(value >> 8) & 15];
                    output[start + 2] = hex[(value >> 4) & 15];
                    output[start + 3] = hex[value & 15];
                }
            });
        }
    }

    private static IEnumerable<string> QueryGrams(string term)
    {
        var folded = term.ToUpperInvariant();
        return Grams(folded, Math.Min(3, folded.Length));
    }

    private static string IndexedGrams(string name)
    {
        var folded = name.ToUpperInvariant();
        return string.Join(' ', Enumerable.Range(1, 3).SelectMany(length => Grams(folded, length)).Distinct());
    }
}
