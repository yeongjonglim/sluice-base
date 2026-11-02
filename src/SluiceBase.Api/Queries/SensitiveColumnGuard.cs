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
    ITargetEngineRegistry engineRegistry,
    IConfiguration configuration)
{
    // credential is the one the SQL will run with: EXPLAIN checks the same privileges, so an UPDATE in a
    // preview has to be planned with the write credential.
    public async Task<SensitiveColumnDecision> EvaluateAsync(
        UserId userId, DatabaseId databaseId, string sql, CredentialKind credential, CancellationToken ct)
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

        var (touchedHits, unverifiableReason) = await ResolveTouchedAsync(databaseId, sql, credential, sensitiveTuples, ct);

        var touched = touchedHits
            .Select(h => $"{h.Schema}.{h.Table}.{h.Column}")
            .ToArray();

        if (unverifiableReason is not null)
        {
            return new SensitiveColumnDecision([], touched, unverifiableReason);
        }

        if (touchedHits.Count == 0)
        {
            return new SensitiveColumnDecision([], []);
        }

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

    // Statements that cannot return rows. When EXPLAIN cannot plan one of these, the tokenizer check is
    // enough; any other statement EXPLAIN cannot plan (EXECUTE, FETCH, CALL, DO, a SELECT on a table an
    // earlier statement creates, ...) could return data no check can see, so it is blocked instead.
    private static readonly HashSet<string> NoRowOutputStatements = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE", "ALTER", "DROP", "COMMENT", "GRANT", "REVOKE", "TRUNCATE", "REINDEX", "VACUUM", "ANALYZE",
        "CLUSTER", "REFRESH", "SET", "RESET", "LOCK", "SHOW",
    };

    // Returns the sensitive columns the SQL actually touches, using EXPLAIN-based resolution per statement.
    // UnverifiableReason is set when a statement could return data that cannot be checked: the query must
    // then be blocked, since the tokenizer can be fooled and is only trusted for statements without rows.
    private async Task<(IReadOnlyList<SensitiveColumnHit> Hits, string? UnverifiableReason)> ResolveTouchedAsync(
        DatabaseId databaseId,
        string sql,
        CredentialKind credential,
        IReadOnlyList<(string Schema, string Table, string Column)> sensitiveTuples,
        CancellationToken ct)
    {
        var hits = new HashSet<SensitiveColumnHit>();

        // Bound EXPLAIN-based resolution the same way IQueryService bounds execution — a
        // pathological statement shouldn't be able to tie up a connection for the ambient
        // request timeout. A resolution timeout degrades to the tokenizer fallback below rather
        // than failing the request.
        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        ITargetEngine engine;
        string connectionString;
        try
        {
            var database = await db.Databases.AsNoTracking()
                .Include(d => d.Server)
                .SingleAsync(d => d.Id == databaseId, token);
            engine = engineRegistry.Resolve(database.Server!.Kind);
            connectionString = await connectionFactory.GetConnectionStringAsync(
                databaseId, credential, token);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Cannot connect / resolve engine, or resolution timed out. A genuine caller cancellation (ct)
            // still propagates.
            return ([], "The query could not be checked against the sensitive-column policy because the database " +
                        "could not be reached, so it was blocked.");
        }

        foreach (var statement in SqlStatementSplitter.Split(sql))
        {
            try
            {
                var resolved = await engine.ResolveReferencedColumnsAsync(connectionString, statement, token);
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
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // EXPLAIN failed for this statement (unplannable, permission, opaque, or resolution timeout).
                // A genuine caller cancellation (ct) still propagates.
                var keyword = SqlLexing.LeadingWords(statement, 1).FirstOrDefault() ?? "";
                if (!NoRowOutputStatements.Contains(keyword))
                {
                    var why = ex is OperationCanceledException
                        ? "it could not be planned in time"
                        : ex.Message.Split('\n')[0];
                    return ([.. hits],
                        "A statement could not be checked against the sensitive-column policy, so the query was " +
                        $"blocked ({why}).");
                }

                foreach (var h in SqlColumnChecker.FindBlockedColumns(statement, sensitiveTuples))
                {
                    hits.Add(h);
                }
            }
        }

        return ([.. hits], null);
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
