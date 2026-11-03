namespace SluiceBase.Api.Queries;

internal static class SqlLexing
{
    // True when the quote at quotePos opens an E'...' escape string: it follows a lone E, not one that ends a
    // longer identifier such as "name'...'".
    public static bool IsEscapeStringPrefix(string sql, int quotePos) =>
        quotePos >= 1
        && sql[quotePos - 1] is 'E' or 'e'
        && (quotePos < 2 || !IsIdentifierChar(sql[quotePos - 2]));

    public static bool IsIdentifierChar(char c) => c == '_' || c == '$' || char.IsLetterOrDigit(c);

    // The first words of a statement, skipping whitespace and comments (block comments nest in Postgres).
    public static List<string> LeadingWords(string statement, int count)
    {
        var words = new List<string>(count);
        var pos = 0;
        var len = statement.Length;
        while (words.Count < count && pos < len)
        {
            var c = statement[pos];
            if (char.IsWhiteSpace(c))
            {
                pos++;
            }
            else if (c == '-' && pos + 1 < len && statement[pos + 1] == '-')
            {
                while (pos < len && statement[pos] != '\n')
                {
                    pos++;
                }
            }
            else if (c == '/' && pos + 1 < len && statement[pos + 1] == '*')
            {
                pos += 2;
                var depth = 1;
                while (pos < len && depth > 0)
                {
                    if (statement[pos] == '/' && pos + 1 < len && statement[pos + 1] == '*')
                    {
                        depth++;
                        pos += 2;
                    }
                    else if (statement[pos] == '*' && pos + 1 < len && statement[pos + 1] == '/')
                    {
                        depth--;
                        pos += 2;
                    }
                    else
                    {
                        pos++;
                    }
                }
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = pos;
                while (pos < len && (char.IsLetterOrDigit(statement[pos]) || statement[pos] == '_'))
                {
                    pos++;
                }

                words.Add(statement[start..pos]);
            }
            else
            {
                break;
            }
        }

        return words;
    }
}
