using System.Text.Json;
using System.Text.RegularExpressions;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Queries;

// Walks the JSON produced by `EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)` and returns the
// base-table columns the statement touches, fully qualified. Attribution comes from each
// scan node's real Schema + Relation Name, resolved through the node's Alias — so columns
// are never mis-attributed by bare name. A `<alias>.*` reference (whole-row var) yields a
// ColumnRef with Column == "*", meaning "every column of this relation".
internal static partial class PostgresPlanColumnExtractor
{
    // Property names whose string/array values carry alias-qualified column references.
    private static readonly string[] ExpressionFields =
    [
        "Output", "Filter", "Index Cond", "Recheck Cond", "Hash Cond", "Merge Cond",
        "Join Filter", "Sort Key", "Group Key", "Presorted Key", "Cache Key",
        "TID Cond", "One-Time Filter", "Order By",
    ];

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
        Walk(root, hits);
        return [.. hits];
    }

    // Post-order walk. Returns the alias -> (schema, relation) map for THIS node's subtree, and
    // resolves this node's own expression fields against that subtree map — so a reference is
    // only ever attributed to a relation within its own branch, never a same-named alias in a
    // sibling branch (e.g. a UNION arm). Column hits accumulate into `hits`.
    private static Dictionary<string, (string Schema, string Relation)> Walk(
        JsonElement node, HashSet<ColumnRef> hits)
    {
        var map = new Dictionary<string, (string Schema, string Relation)>(StringComparer.Ordinal);

        if (node.TryGetProperty("Plans", out var plans) && plans.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in plans.EnumerateArray())
            {
                foreach (var kv in Walk(child, hits))
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

        foreach (var field in ExpressionFields)
        {
            if (!node.TryGetProperty(field, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                ScanExpression(value.GetString()!, map, hits);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        ScanExpression(el.GetString()!, map, hits);
                    }
                }
            }
        }

        return map;
    }

    private static void ScanExpression(
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

    private static string Unquote(string token) =>
        token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1].Replace("\"\"", "\"")
            : token;
}
