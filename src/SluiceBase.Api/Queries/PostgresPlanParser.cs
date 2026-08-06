using System.Text.Json;
using SluiceBase.Core.Queries;

namespace SluiceBase.Api.Queries;

// Parses the single JSON document produced by `EXPLAIN (FORMAT JSON ...)`.
// The document is an array with one element: { "Plan": {...}, "Execution Time": n? }.
internal static class PostgresPlanParser
{
    public static QueryPlanSummary Parse(string planJson)
    {
        using var doc = JsonDocument.Parse(planJson);
        var root = doc.RootElement[0];
        var plan = root.GetProperty("Plan");

        var totalCost = plan.TryGetProperty("Total Cost", out var tc) ? tc.GetDouble() : 0;
        var estimatedRows = plan.TryGetProperty("Plan Rows", out var pr) ? pr.GetDouble() : 0;
        var rootNode = plan.TryGetProperty("Node Type", out var nt) ? nt.GetString() ?? "" : "";
        var hasSeqScan = HasSeqScan(plan);

        // "Execution Time" is only present with ANALYZE.
        double? actualTotalMs = root.TryGetProperty("Execution Time", out var et)
            ? et.GetDouble()
            : null;

        return new QueryPlanSummary(totalCost, estimatedRows, rootNode, hasSeqScan, actualTotalMs);
    }

    private static bool HasSeqScan(JsonElement node)
    {
        if (node.TryGetProperty("Node Type", out var nt) && nt.GetString() == "Seq Scan")
        {
            return true;
        }

        if (node.TryGetProperty("Plans", out var plans) && plans.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in plans.EnumerateArray())
            {
                if (HasSeqScan(child))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
