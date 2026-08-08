using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Queries;

// Result of evaluating a SQL string against a user's sensitive-column policy.
// BlockedHits empty && PolicyBlockReason null => allowed. Touched is the full set of
// sensitive columns the SQL references (for audit), regardless of the user's bypass grants.
// PolicyBlockReason is set when a denylisted serialization function blocks the query outright.
internal sealed record SensitiveColumnDecision(
    IReadOnlyList<SensitiveColumnHit> BlockedHits,
    IReadOnlyList<string> Touched,
    string? PolicyBlockReason = null);

internal sealed class SensitiveColumnGuard(
    AppDbContext db,
    IServerConnectionFactory connectionFactory,
    ITargetEngineRegistry engineRegistry)
{
    public async Task<SensitiveColumnDecision> EvaluateAsync(
        UserId userId, DatabaseId databaseId, string sql, CancellationToken ct)
    {
        var sensitiveColumns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == databaseId)
            .ToListAsync(ct);

        if (sensitiveColumns.Count == 0)
        {
            return new SensitiveColumnDecision([], []);
        }

        // Opaque XML-export functions can serialize a table named by string literal; neither
        // EXPLAIN nor the tokenizer can see the columns, so block by name up front.
        var denied = SerializationFunctionDenylist.FindFirst(sql);
        if (denied is not null)
        {
            return new SensitiveColumnDecision([], [],
                $"Query uses {denied}(), which can serialize table data that cannot be verified " +
                "against the sensitive-column policy.");
        }

        var sensitiveTuples = sensitiveColumns
            .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
            .ToList();

        var touchedHits = await ResolveTouchedAsync(databaseId, sql, sensitiveTuples, ct);
        if (touchedHits.Count == 0)
        {
            return new SensitiveColumnDecision([], []);
        }

        var touched = touchedHits
            .Select(h => $"{h.Schema}.{h.Table}.{h.Column}")
            .ToArray();

        var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
        var bypassed = await db.UserColumnBypasses
            .AsNoTracking()
            .Where(b => b.UserId == userId && sensitiveColumnIds.Contains(b.SensitiveColumnId))
            .Join(db.SensitiveColumns, b => b.SensitiveColumnId, c => c.Id,
                (b, c) => new { c.SchemaName, c.TableName, c.ColumnName })
            .ToListAsync(ct);
        var bypassedSet = bypassed
            .Select(k => (k.SchemaName, k.TableName, k.ColumnName))
            .ToHashSet();

        var blockedHits = touchedHits
            .Where(h => !bypassedSet.Contains((h.Schema, h.Table, h.Column)))
            .ToList();

        return new SensitiveColumnDecision(blockedHits, touched);
    }

    // Returns the sensitive columns the SQL actually touches. Uses EXPLAIN-based resolution per
    // statement; on any failure for a statement, falls back to the SqlColumnChecker tokenizer
    // (conservative — never under-blocks).
    private async Task<IReadOnlyList<SensitiveColumnHit>> ResolveTouchedAsync(
        DatabaseId databaseId,
        string sql,
        IReadOnlyList<(string Schema, string Table, string Column)> sensitiveTuples,
        CancellationToken ct)
    {
        var hits = new HashSet<SensitiveColumnHit>();

        ITargetEngine engine;
        string connectionString;
        try
        {
            var database = await db.Databases.AsNoTracking()
                .Include(d => d.Server)
                .SingleAsync(d => d.Id == databaseId, ct);
            engine = engineRegistry.Resolve(database.Server!.Kind);
            connectionString = await connectionFactory.GetConnectionStringAsync(
                databaseId, CredentialKind.Read, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cannot connect / resolve engine — tokenizer over the whole SQL keeps us safe.
            foreach (var h in SqlColumnChecker.FindBlockedColumns(sql, sensitiveTuples))
            {
                hits.Add(h);
            }
            return [.. hits];
        }

        foreach (var statement in SqlStatementSplitter.Split(sql))
        {
            try
            {
                var resolved = await engine.ResolveReferencedColumnsAsync(connectionString, statement, ct);
                foreach (var (schema, table, column) in sensitiveTuples)
                {
                    var isTouched = resolved.Any(r =>
                        Eq(r.Schema, schema) && Eq(r.Table, table) &&
                        (r.Column == "*" || Eq(r.Column, column)));
                    if (isTouched)
                    {
                        hits.Add(new SensitiveColumnHit(schema, table, column));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // EXPLAIN failed for this statement (unplannable, permission, opaque) —
                // conservative tokenizer fallback for this statement only.
                foreach (var h in SqlColumnChecker.FindBlockedColumns(statement, sensitiveTuples))
                {
                    hits.Add(h);
                }
            }
        }

        return [.. hits];
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
