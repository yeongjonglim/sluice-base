namespace SluiceBase.Api.Queries;

public sealed record SensitiveColumnHit(string Schema, string Table, string Column);

internal static class SqlColumnChecker
{
    // PostgreSQL functions that serialize entire rows, bypassing column-level checks.
    private static readonly HashSet<string> RowSerializationFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "to_jsonb",
        "to_json",
        "row_to_json",
        "jsonb_build_object",
        "json_build_object",
        "jsonb_agg",
        "json_agg",
        "array_to_json",
        "hstore",
        "row",
    };

    public static IReadOnlyList<SensitiveColumnHit> FindBlockedColumns(
        string sql,
        IReadOnlyList<(string Schema, string Table, string Column)> blockedColumns)
    {
        if (blockedColumns.Count == 0)
        {
            return [];
        }

        var tokenResult = SqlTokenizer.Tokenize(sql);
        var hits = new HashSet<SensitiveColumnHit>();

        // SELECT * — conservatively block all sensitive columns.
        // We cannot know which tables * expands to without a live schema lookup,
        // so any wildcard in a query with sensitive columns is blocked.
        if (tokenResult.HasWildcard)
        {
            foreach (var (schema, table, column) in blockedColumns)
            {
                hits.Add(new(schema, table, column));
            }
            return [.. hits];
        }

        // Row-serialization functions (e.g. to_jsonb(row)) can leak all columns
        // without naming them individually. If any such function is present and the
        // query references a table with blocked columns, block all sensitive columns
        // for that table.
        if (HasRowSerializationFunction(tokenResult.Identifiers))
        {
            var referencedTables = blockedColumns
                .Where(bc => tokenResult.Identifiers.Any(id =>
                    string.Equals(bc.Table, id, StringComparison.OrdinalIgnoreCase)))
                .Select(bc => (bc.Schema, bc.Table))
                .ToHashSet();

            if (referencedTables.Count > 0)
            {
                foreach (var (schema, table, column) in blockedColumns)
                {
                    if (referencedTables.Contains((schema, table)))
                    {
                        hits.Add(new(schema, table, column));
                    }
                }
                return [.. hits];
            }
        }

        // Check identifier tokens against blocked column names (case-insensitive).
        // Table/schema qualification is not available after tokenization, so any
        // identifier matching a blocked column name is treated as a hit.
        foreach (var identifier in tokenResult.Identifiers)
        {
            foreach (var (schema, table, column) in blockedColumns)
            {
                if (string.Equals(column, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new(schema, table, column));
                }
            }
        }

        return [.. hits];
    }

    private static bool HasRowSerializationFunction(IReadOnlyList<string> identifiers)
    {
        foreach (var id in identifiers)
        {
            if (RowSerializationFunctions.Contains(id))
            {
                return true;
            }
        }
        return false;
    }
}
