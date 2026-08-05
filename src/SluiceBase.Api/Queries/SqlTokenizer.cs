using System.Text;

namespace SluiceBase.Api.Queries;

internal static class SqlTokenizer
{
    public sealed record Result(IReadOnlyList<string> Identifiers, bool HasWildcard);

    // Classification of the previous significant token, used to tell a column-expansion
    // wildcard (`SELECT *`, `t.*`) apart from a `*` that does NOT expand columns:
    // multiplication (`a * b`) or the count aggregate (`count(*)`).
    private enum Prev
    {
        // Start of input, SELECT/DISTINCT/ALL, `,`, `.`, `)`, or an operator — a `*`
        // here expands columns. `)` is treated conservatively as a wildcard position so
        // `DISTINCT ON (x) *` still blocks (at the cost of over-blocking `(a + b) * c`).
        Wildcard,
        // A value operand (non-keyword identifier or number) — a following `*` is multiplication.
        Value,
        // An opening paren — a following `*` is the count(*) argument, not a column expansion.
        OpenParen,
    }

    // Keywords after which a `*` is a column-expansion wildcard rather than multiplication.
    private static readonly HashSet<string> WildcardKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT",
        "DISTINCT",
        "ALL",
    };

    public static Result Tokenize(string sql)
    {
        var identifiers = new List<string>();
        var hasWildcard = false;
        var prev = Prev.Wildcard;
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
                    prev = Prev.Value; // string literal is a value operand
                }
                else
                {
                    pos++; // lone $ — skip as punctuation
                    prev = Prev.Wildcard;
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
                prev = Prev.Value; // string literal is a value operand
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
                prev = Prev.Value; // quoted identifier is a value operand, never a keyword
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
                    prev = Prev.Value; // prefix string is a value operand
                    continue;
                }
                var word = sql[start..pos];
                identifiers.Add(word);
                // SELECT/DISTINCT/ALL keep a following `*` as a wildcard; any other
                // identifier is a value operand, so a following `*` is multiplication.
                prev = WildcardKeywords.Contains(word) ? Prev.Wildcard : Prev.Value;
                continue;
            }

            // Wildcard vs. multiplication vs. count(*): only a `*` in a column-expansion
            // position (after SELECT, `,`, `.`, `)`, or start) expands to every column.
            // A `*` after a value operand is multiplication; after `(` it is count(*).
            if (c == '*')
            {
                if (prev != Prev.Value && prev != Prev.OpenParen)
                {
                    hasWildcard = true;
                }
                prev = Prev.Value;
                pos++;
                continue;
            }

            // Everything else (operators, punctuation, digits): skip, but record whether
            // it can precede a column-expansion wildcard. Whitespace is insignificant and
            // leaves the previous token's classification untouched.
            if (!char.IsWhiteSpace(c))
            {
                prev = c switch
                {
                    '(' => Prev.OpenParen,
                    _ when char.IsDigit(c) => Prev.Value,
                    _ => Prev.Wildcard, // ) , . and operators are all wildcard positions
                };
            }
            pos++;
        }

        return new Result(identifiers, hasWildcard);
    }
}
