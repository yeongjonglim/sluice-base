using System.Text;

namespace SluiceBase.Api.Queries;

internal static class SqlTokenizer
{
    public sealed record Result(IReadOnlyList<string> Identifiers, bool HasWildcard);

    public static Result Tokenize(string sql)
    {
        var identifiers = new List<string>();
        var hasWildcard = false;
        var pos = 0;
        var len = sql.Length;

        while (pos < len)
        {
            var c = sql[pos];

            // Line comment: -- to end of line
            if (c == '-' && pos + 1 < len && sql[pos + 1] == '-')
            {
                pos += 2;
                while (pos < len && sql[pos] != '\n')
                {
                    pos++;
                }
                continue;
            }

            // Block comment: /* ... */ with PostgreSQL nesting support
            if (c == '/' && pos + 1 < len && sql[pos + 1] == '*')
            {
                pos += 2;
                var depth = 1;
                while (pos + 1 < len && depth > 0)
                {
                    if (sql[pos] == '/' && sql[pos + 1] == '*')
                    {
                        depth++;
                        pos += 2;
                    }
                    else if (sql[pos] == '*' && sql[pos + 1] == '/')
                    {
                        depth--;
                        pos += 2;
                    }
                    else
                    {
                        pos++;
                    }
                }
                continue;
            }

            // Dollar-quoted string: $tag$...$tag$ (tag may be empty)
            if (c == '$')
            {
                var tagEnd = pos + 1;
                while (tagEnd < len && (sql[tagEnd] == '_' || char.IsLetterOrDigit(sql[tagEnd])))
                {
                    tagEnd++;
                }
                if (tagEnd < len && sql[tagEnd] == '$')
                {
                    var tag = sql[pos..(tagEnd + 1)];
                    pos = tagEnd + 1;
                    var close = sql.IndexOf(tag, pos, StringComparison.Ordinal);
                    pos = close >= 0 ? close + tag.Length : len;
                }
                else
                {
                    pos++; // lone $ — skip as punctuation
                }
                continue;
            }

            // Single-quoted string: '...' with '' as escape
            if (c == '\'')
            {
                pos++;
                while (pos < len)
                {
                    if (sql[pos] == '\'')
                    {
                        pos++;
                        if (pos < len && sql[pos] == '\'')
                        {
                            pos++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        pos++;
                    }
                }
                continue;
            }

            // Double-quoted identifier: "..." with "" as escape — emit as identifier
            if (c == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < len)
                {
                    if (sql[pos] == '"')
                    {
                        pos++;
                        if (pos < len && sql[pos] == '"')
                        {
                            sb.Append('"');
                            pos++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(sql[pos++]);
                    }
                }
                if (sb.Length > 0)
                {
                    identifiers.Add(sb.ToString());
                }
                continue;
            }

            // Unquoted identifier or prefix string (E'...', B'...', X'...', N'...')
            if (c == '_' || char.IsLetter(c))
            {
                var start = pos;
                while (pos < len && (sql[pos] == '_' || char.IsLetterOrDigit(sql[pos])))
                {
                    pos++;
                }
                if (pos < len && sql[pos] == '\'')
                {
                    // Prefix string — skip the following string literal
                    pos++;
                    while (pos < len)
                    {
                        if (sql[pos] == '\'')
                        {
                            pos++;
                            if (pos < len && sql[pos] == '\'')
                            {
                                pos++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            pos++;
                        }
                    }
                    continue;
                }
                identifiers.Add(sql[start..pos]);
                continue;
            }

            // Wildcard
            if (c == '*')
            {
                hasWildcard = true;
                pos++;
                continue;
            }

            // Everything else (operators, punctuation, digits): skip
            pos++;
        }

        return new Result(identifiers, hasWildcard);
    }
}
