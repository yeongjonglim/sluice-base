namespace SluiceBase.Api.Queries;

public sealed record SensitiveColumnHit(string Schema, string Table, string Column);

internal static class SqlColumnChecker
{
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
}
