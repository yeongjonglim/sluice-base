using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Queries;

// Walks the JSON produced by `EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)` and returns the
// base-table columns the statement touches, attributed to their real relation.
//
// Two sources of column references, because PostgreSQL qualifies inconsistently:
//   1. Qualified refs (`alias.col`, `alias.*`) — appear in joins, WHERE/JOIN/GROUP/ORDER
//      conditions, views, and whole-row vars. Harvested from EVERY node's expression fields
//      and resolved through the node's own subtree alias map, so a reference is never
//      mis-attributed to a same-named alias in a sibling branch (e.g. a UNION arm).
//   2. Bare column names (`email`) — PostgreSQL emits these UNQUALIFIED in a scan/plan
//      `Output` list whenever there is a single base relation in scope (it only qualifies to
//      disambiguate ≥2 relations). They are harvested ONLY from the ROOT node's `Output` and
//      attributed to the single relation. Bare names are deliberately NOT harvested from
//      child scan nodes: under count(*)/DISTINCT/GROUP BY/aggregates a child scan's `Output`
//      is the full physical tuple (every column), which the query does not actually expose —
//      harvesting it would over-block. The root `Output` reflects only what the query returns
//      (`["count(*)"]`, `["max(ssn)"]`, `["email"]`, `["id","name","email","ssn"]`).
//
// A `<alias>.*` reference (whole-row var) yields a ColumnRef with Column == "*", meaning
// "every column of this relation".
internal static partial class PostgresPlanColumnExtractor
{
    // Condition/key fields carry alias-qualified column references for columns USED in
    // computation (WHERE/JOIN/GROUP/ORDER). They are always meaningful and always harvested.
    private static readonly string[] ConditionFields =
    [
        "Filter", "Index Cond", "Recheck Cond", "Hash Cond", "Merge Cond",
        "Join Filter", "Sort Key", "Group Key", "Presorted Key", "Cache Key",
        "TID Cond", "One-Time Filter", "Order By",
    ];

    // Parent node types under which a scan's `Output` is the full physical tuple (every column
    // of the relation) rather than the query's actual projection — a PostgreSQL execution
    // artifact. A scan's `Output` is NOT harvested when its parent is one of these, or count(*)
    // and "join to a table using only its id" would over-block on unread columns. Columns that
    // are genuinely exposed still reach the root Output; columns genuinely used still reach a
    // ConditionField — so skipping these Outputs never under-blocks.
    private static readonly HashSet<string> PhysicalTupleParents = new(StringComparer.Ordinal)
    {
        "Aggregate", "Group", "Hash Join", "Merge Join", "Nested Loop",
    };

    [GeneratedRegex("""(?<alias>"(?:[^"]|"")*"|[A-Za-z_][A-Za-z0-9_$]*)\.(?<col>"(?:[^"]|"")*"|\*|[A-Za-z_][A-Za-z0-9_$]*)""")]
    private static partial Regex QualifiedRef();

    public static IReadOnlyList<ColumnRef> Extract(string planJson)
    {
        using var doc = JsonDocument.Parse(planJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return [];
        }

        var root = doc.RootElement[0].GetProperty("Plan");
        var hits = new HashSet<ColumnRef>();
        Walk(root, parentNodeType: null, hits);
        return [.. hits];
    }

    // Post-order walk. Returns the alias -> (schema, relation) map for THIS node's subtree.
    //   - ConditionFields (WHERE/JOIN/GROUP/ORDER expressions) are harvested for qualified refs
    //     at EVERY node — those are the columns used in computation.
    //   - The node's `Output` (qualified refs + bare column names) is harvested UNLESS the
    //     node's parent emits a full physical tuple (PhysicalTupleParents) — that Output would
    //     be unread columns, not the query's projection.
    // Bare column names are attributed to the sole relation in this node's subtree, the only
    // situation in which PostgreSQL leaves a column unqualified.
    private static Dictionary<string, (string Schema, string Relation)> Walk(
        JsonElement node, string? parentNodeType, HashSet<ColumnRef> hits)
    {
        var map = new Dictionary<string, (string Schema, string Relation)>(StringComparer.Ordinal);
        var nodeType = node.TryGetProperty("Node Type", out var nt) ? nt.GetString() : null;

        if (node.TryGetProperty("Plans", out var plans) && plans.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in plans.EnumerateArray())
            {
                foreach (var kv in Walk(child, nodeType, hits))
                {
                    map[kv.Key] = kv.Value;
                }
            }
        }

        if (node.TryGetProperty("Relation Name", out var rel) && node.TryGetProperty("Schema", out var schema))
        {
            var alias = node.TryGetProperty("Alias", out var a) ? a.GetString() : rel.GetString();
            if (!string.IsNullOrEmpty(alias))
            {
                map[alias!] = (schema.GetString() ?? "", rel.GetString() ?? "");
            }
        }

        foreach (var field in ConditionFields)
        {
            ForEachString(node, field, s => ScanQualified(s, map, hits));
        }

        if (parentNodeType is null || !PhysicalTupleParents.Contains(parentNodeType))
        {
            var relations = new HashSet<(string Schema, string Relation)>(map.Values);
            var single = relations.Count == 1 ? relations.First() : default((string Schema, string Relation)?);
            ForEachString(node, "Output", s =>
            {
                ScanQualified(s, map, hits);
                if (single is { } r)
                {
                    AddBareColumns(s, r.Schema, r.Relation, hits);
                }
            });
        }

        return map;
    }

    private static void ForEachString(JsonElement node, string field, Action<string> action)
    {
        if (!node.TryGetProperty(field, out var value))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            action(value.GetString()!);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in value.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    action(el.GetString()!);
                }
            }
        }
    }

    private static void ScanQualified(
        string expr, Dictionary<string, (string Schema, string Relation)> map, HashSet<ColumnRef> hits)
    {
        foreach (Match m in QualifiedRef().Matches(expr))
        {
            var alias = Unquote(m.Groups["alias"].Value);
            if (!map.TryGetValue(alias, out var rel))
            {
                continue; // not a base-relation alias (e.g. a function or CTE name)
            }

            var col = m.Groups["col"].Value == "*" ? "*" : Unquote(m.Groups["col"].Value);
            hits.Add(new ColumnRef(rel.Schema, rel.Relation, col));
        }
    }

    // Extracts bare (unqualified) column identifiers from one EXPLAIN expression string,
    // attributing each to (schema, relation). Skips: single-quoted string literals; the type
    // name after a `::` cast; function names (identifier immediately followed by `(`); and
    // either side of a qualified `alias.col` reference (handled by ScanQualified). No keyword
    // stop-list: a keyword that collides with a column name (e.g. "name") must still be caught,
    // and a genuine keyword only over-blocks if a sensitive column shares its name (safe).
    private static void AddBareColumns(string expr, string schema, string relation, HashSet<ColumnRef> hits)
    {
        var i = 0;
        var n = expr.Length;
        while (i < n)
        {
            var c = expr[i];

            // Single-quoted string literal.
            if (c == '\'')
            {
                i++;
                while (i < n)
                {
                    if (expr[i] == '\'')
                    {
                        i++;
                        if (i < n && expr[i] == '\'') { i++; } else { break; }
                    }
                    else { i++; }
                }
                continue;
            }

            // `::` cast — skip the following type name (identifier, may be schema-qualified,
            // quoted, and carry array brackets).
            if (c == ':' && i + 1 < n && expr[i + 1] == ':')
            {
                i += 2;
                while (i < n && (char.IsLetterOrDigit(expr[i]) || expr[i] is '_' or '.' or '"' or '[' or ']')) { i++; }
                continue;
            }

            // Quoted identifier.
            if (c == '"')
            {
                var start = i;
                i++;
                var sb = new StringBuilder();
                while (i < n)
                {
                    if (expr[i] == '"')
                    {
                        i++;
                        if (i < n && expr[i] == '"') { sb.Append('"'); i++; } else { break; }
                    }
                    else { sb.Append(expr[i++]); }
                }
                MaybeAdd(sb.ToString(), start, i, expr, schema, relation, hits);
                continue;
            }

            // Unquoted identifier.
            if (c == '_' || char.IsLetter(c))
            {
                var start = i;
                while (i < n && (expr[i] == '_' || expr[i] == '$' || char.IsLetterOrDigit(expr[i]))) { i++; }
                MaybeAdd(expr[start..i], start, i, expr, schema, relation, hits);
                continue;
            }

            i++;
        }
    }

    private static void MaybeAdd(
        string id, int start, int end, string expr, string schema, string relation, HashSet<ColumnRef> hits)
    {
        if (id.Length == 0)
        {
            return;
        }

        var prev = start > 0 ? expr[start - 1] : '\0';
        var next = end < expr.Length ? expr[end] : '\0';

        // Part of a qualified `alias.col` (handled by ScanQualified), or a function name.
        if (prev == '.' || next == '.' || next == '(')
        {
            return;
        }

        hits.Add(new ColumnRef(schema, relation, id));
    }

    private static string Unquote(string token) =>
        token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1].Replace("\"\"", "\"")
            : token;
}
