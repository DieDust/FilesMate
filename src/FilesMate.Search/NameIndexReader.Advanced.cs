using Microsoft.Data.Sqlite;

namespace FilesMate.Search;

public static partial class NameIndexReader
{
    public static AdvancedSearchResponse SearchAdvanced(string database, SearchPageRequest request,
        AdvancedSearchQuery query, int limit, int offset, CancellationToken token,
        Func<string, bool, bool>? include = null, Func<string, bool, int>? rank = null, bool includeTotal = true)
    {
        token.ThrowIfCancellationRequested();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 1 }.ToString());
        connection.Open();
        connection.CreateCollation("fm_nocase", (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a, b));
        connection.CreateCollation("fm_ordinal", (a, b) => StringComparer.Ordinal.Compare(a, b));
        using var interrupt = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT name FROM sqlite_master WHERE name IN ('file_metadata','file_name_gram');";
        var tables = new HashSet<string>();
        using (var reader = schema.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        var metadata = tables.Contains("file_metadata");
        if (!metadata && (query.RequiresMetadata || request.Sort is SearchResultSort.SizeDescending or SearchResultSort.ModifiedDescending))
            return new([], false, "SearchPage_MetadataRequired");
        if (query.RequiresMetadata)
        {
            connection.CreateFunction<string, string, long, long?, long?, bool>("fm_advanced", (name, path, dir, size, modified) =>
            {
                token.ThrowIfCancellationRequested();
                return query.Matches(new(name, path, dir != 0, size, modified)) && (include?.Invoke(path, dir != 0) ?? true);
            });
        }
        else
        {
            // Only the parsed predicate determines which arguments it needs.
            // Metadata still participates in projection, sorting and result counts.
            connection.CreateFunction<string, string, long, bool>("fm_advanced", (name, path, dir) =>
            {
                token.ThrowIfCancellationRequested();
                return query.Matches(new(name, path, dir != 0)) && (include?.Invoke(path, dir != 0) ?? true);
            });
        }
        connection.CreateFunction<string, long, int>("fm_priority", (path, dir) => rank?.Invoke(path, dir != 0) ?? 0);
        var match = string.Join(" AND ", query.IndexTerms.SelectMany(QueryGrams).Distinct().Select(g => '"' + g + '"'));
        var useGrams = tables.Contains("file_name_gram") && match.Length > 0;
        using var command = connection.CreateCommand();
        var order = request.Sort switch
        {
            SearchResultSort.Name => "f.name COLLATE NOCASE",
            SearchResultSort.NameDescending => "f.name COLLATE NOCASE DESC",
            SearchResultSort.ModifiedDescending => "m.modified DESC, f.name COLLATE NOCASE",
            SearchResultSort.SizeDescending => "m.size DESC, f.name COLLATE NOCASE",
            SearchResultSort.Path => "f.path COLLATE NOCASE",
            _ => "fm_priority(f.path, CAST(f.is_dir AS INTEGER)), length(f.name), f.name COLLATE NOCASE",
        };
        var columns = metadata ? "m.size, m.modified" : "NULL, NULL";
        var predicateMetadata = query.RequiresMetadata ? ", " + columns : "";
        // MATCH runs once, before the rowid lookups. File-first plans restart
        // the virtual FTS query for every indexed file.
        var source = useGrams ? "file_name_gram g CROSS JOIN file_name f ON f.rowid=g.rowid " : "file_name f ";
        command.CommandText = "SELECT f.name, f.path, CAST(f.is_dir AS INTEGER), " + columns + (includeTotal ? ", COUNT(*) OVER()" : "") + " FROM " + source
            + (metadata ? "LEFT JOIN file_metadata m ON m.rowid=f.rowid " : "")
            + (useGrams ? "WHERE file_name_gram MATCH $match AND " : "WHERE ")
            + $"fm_advanced(f.name, f.path, CAST(f.is_dir AS INTEGER){predicateMetadata}) ORDER BY {order.Replace("NOCASE", "fm_nocase")}, f.path COLLATE fm_ordinal LIMIT $limit OFFSET $offset;";
        if (useGrams) command.Parameters.AddWithValue("$match", match);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 20000) + 1);
        command.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        var hits = new List<AdvancedSearchHit>();
        long? total = includeTotal ? 0 : null;
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (includeTotal) total = reader.GetInt64(5);
                hits.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0,
                    reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetInt64(4)));
            }
        }
        catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        return new(hits.Take(limit).ToArray(), hits.Count > limit, metadata ? null : "SearchPage_MetadataMissing", total);
    }
}
