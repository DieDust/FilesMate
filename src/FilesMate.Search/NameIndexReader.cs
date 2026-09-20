using Microsoft.Data.Sqlite;
using System.Text;

namespace FilesMate.Search;

public sealed record NameHit(string Name, string Path, bool IsDirectory, ApplicationEntry? Application)
{
    public NameHit(string name, string path, bool isDirectory) : this(name, path, isDirectory, null) { }
    public void Deconstruct(out string name, out string path, out bool isDirectory)
    { name = Name; path = Path; isDirectory = IsDirectory; }
}

/// <summary>Shared filename semantics for the app and the executable Flow plugin.</summary>
public static class NameIndexReader
{
    /// <summary>Most rows an indexed (trigram) query is ranked over before paging.</summary>
    public const int IndexedCandidateCap = 20000;

    /// <summary>Most rows a full-scan (one- or two-character) query is ranked over before paging.</summary>
    public const int ScanCandidateCap = 4000;

    public static string[] Terms(string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static bool Matches(string name, IReadOnlyList<string> terms) =>
        terms.Count > 0 && terms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

    public static int Relevance(string name, string query) =>
        name.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 0
        : Path.GetFileNameWithoutExtension(name).Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 1
        : name.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 2 : 3;

    public static IReadOnlyList<NameHit> Search(string database, string query, string? prefix, int limit, CancellationToken token = default,
        Func<string, bool, int>? rank = null, Func<string, bool, bool>? filter = null, int offset = 0, bool rankByPath = false)
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
        connection.CreateFunction<string, bool>("fm_match", name => Matches(name, terms), isDeterministic: true);
        // Rank before LIMIT so callers can prioritize launchable results without
        // losing them to a large number of matching documents or directories.
        connection.CreateFunction<string, string, int>("fm_rank",
            (name, isDirectory) => rank?.Invoke(rankByPath ? name : Path.GetFileName(name), isDirectory == "1") ?? Relevance(Path.GetFileName(name), query), isDeterministic: true);
        connection.CreateFunction<string, bool>("fm_scope", path => prefix is null || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
        connection.CreateFunction<string, string, bool>("fm_filter", (name, directory) => filter?.Invoke(name, directory == "1") ?? true, isDeterministic: true);
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE name='file_name_substring');";
        var indexed = Convert.ToInt64(schema.ExecuteScalar()) != 0;
        // A quoted trigram phrase is literal; one/two-character terms still use
        // the same ordinal substring predicate, rather than disappearing from FTS.
        var match = string.Join(" AND ", terms.Where(t => t.EnumerateRunes().Count() >= 3)
            .Select(t => "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        using var command = connection.CreateCommand();
        var useIndex = indexed && match.Length > 0;
        // Ranking runs a managed callback per candidate, so bound the candidate set before ORDER BY. A one- or
        // two-character query cannot use the trigram index and would otherwise scan and rank the whole table
        // ("a" matches nearly everything); an indexed query with a very common fragment is capped more generously.
        // Paging stays consistent because candidates are taken in table order before the sort.
        var candidateCap = useIndex ? IndexedCandidateCap : ScanCandidateCap;
        command.CommandText = """
            SELECT f.name, f.path, f.is_dir FROM (
                SELECT f.name, f.path, f.is_dir FROM file_name f
            """ + (useIndex ? " JOIN file_name_substring s ON s.rowid=f.rowid WHERE file_name_substring MATCH $match AND " : " WHERE ")
            + "fm_scope(f.path) AND fm_match(f.name) AND fm_filter(f.path,f.is_dir) LIMIT $candidates) f "
            + "ORDER BY fm_rank(f.path, f.is_dir), length(f.name), f.name COLLATE NOCASE, f.path LIMIT $limit OFFSET $offset;";
        if (useIndex) command.Parameters.AddWithValue("$match", match);
        command.Parameters.AddWithValue("$candidates", candidateCap);
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
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS file_name_substring
                USING fts5(name, content='', tokenize='trigram');
            INSERT INTO file_name_substring(file_name_substring) VALUES ('delete-all');
            INSERT INTO file_name_substring(rowid,name) SELECT rowid,name FROM file_name;
            INSERT INTO index_meta(key,value) VALUES ('schema','2') ON CONFLICT(key) DO UPDATE SET value='2';
            """;
        try { command.ExecuteNonQuery(); }
        catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
    }
}
