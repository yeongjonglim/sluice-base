namespace SluiceBase.Api.Queries;

// Splits a SQL batch into individual statements on top-level semicolons, skipping any `;`
// inside line comments, block comments (nesting), single-quoted strings, double-quoted
// identifiers, and dollar-quoted strings — the same lexical constructs SqlTokenizer skips.
internal static class SqlStatementSplitter
{
    public static IReadOnlyList<string> Split(string sql)
    {
        var statements = new List<string>();
        var start = 0;
        var pos = 0;
        var len = sql.Length;

        void Emit(int end)
        {
            var stmt = sql[start..end].Trim();
            if (stmt.Length > 0)
            {
                statements.Add(stmt);
            }
        }

        while (pos < len)
        {
            var c = sql[pos];

            if (c == '-' && pos + 1 < len && sql[pos + 1] == '-')
            {
                pos += 2;
                while (pos < len && sql[pos] != '\n') { pos++; }
                continue;
            }
            if (c == '/' && pos + 1 < len && sql[pos + 1] == '*')
            {
                pos += 2;
                var depth = 1;
                while (pos + 1 < len && depth > 0)
                {
                    if (sql[pos] == '/' && sql[pos + 1] == '*') { depth++; pos += 2; }
                    else if (sql[pos] == '*' && sql[pos + 1] == '/') { depth--; pos += 2; }
                    else { pos++; }
                }
                continue;
            }
            if (c == '$')
            {
                var tagEnd = pos + 1;
                while (tagEnd < len && (sql[tagEnd] == '_' || char.IsLetterOrDigit(sql[tagEnd]))) { tagEnd++; }
                if (tagEnd < len && sql[tagEnd] == '$')
                {
                    var tag = sql[pos..(tagEnd + 1)];
                    pos = tagEnd + 1;
                    var close = sql.IndexOf(tag, pos, StringComparison.Ordinal);
                    pos = close >= 0 ? close + tag.Length : len;
                }
                else { pos++; }
                continue;
            }
            if (c == '\'' || c == '"')
            {
                var quote = c;
                pos++;
                while (pos < len)
                {
                    if (sql[pos] == quote)
                    {
                        pos++;
                        if (pos < len && sql[pos] == quote) { pos++; } else { break; }
                    }
                    else { pos++; }
                }
                continue;
            }
            if (c == ';')
            {
                Emit(pos);
                pos++;
                start = pos;
                continue;
            }
            pos++;
        }

        Emit(len);
        return statements;
    }
}
