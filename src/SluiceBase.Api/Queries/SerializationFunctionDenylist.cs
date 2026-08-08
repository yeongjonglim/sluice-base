namespace SluiceBase.Api.Queries;

// PostgreSQL XML-export functions take the target relation/query as a string-literal or
// regclass argument, so the referenced columns never appear as parseable identifiers —
// EXPLAIN sees a bare Function Scan and the tokenizer skips the string. They are blocked by
// name instead of resolved. Detection uses SqlTokenizer identifiers, so a name that appears
// only inside a string literal or comment does not trip the block.
internal static class SerializationFunctionDenylist
{
    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "table_to_xml", "table_to_xmlschema", "table_to_xml_and_xmlschema",
        "query_to_xml", "query_to_xmlschema", "query_to_xml_and_xmlschema",
        "cursor_to_xml", "cursor_to_xmlschema",
        "schema_to_xml", "schema_to_xmlschema", "schema_to_xml_and_xmlschema",
        "database_to_xml", "database_to_xmlschema", "database_to_xml_and_xmlschema",
    };

    public static string? FindFirst(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        foreach (var id in tokens.Identifiers)
        {
            if (Denied.TryGetValue(id, out _))
            {
                return id.ToLowerInvariant();
            }
        }
        return null;
    }
}
