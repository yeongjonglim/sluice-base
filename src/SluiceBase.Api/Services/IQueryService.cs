using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Queries;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Services;

internal enum QueryOutcome { Ok, NotFound, Forbidden, Blocked, BadRequest }

internal sealed record BlockedColumn(string Schema, string Table, string Column);

internal sealed record QueryExecutionResult(
    QueryOutcome Outcome,
    QueryEndpoints.QueryResponse? Response,
    IReadOnlyList<BlockedColumn>? BlockedColumns,
    string? Error);

internal sealed record QueryExplainResult(
    QueryOutcome Outcome,
    QueryPlan? Plan,
    IReadOnlyList<BlockedColumn>? BlockedColumns,
    string? Error);

internal interface IQueryService
{
    Task<QueryExecutionResult> ExecuteAsync(User user, DatabaseId databaseId, string sql, QuerySource source, CancellationToken ct);

    Task<QueryExplainResult> ExplainAsync(User user, DatabaseId databaseId, string sql, bool analyze, CancellationToken ct);
}

internal sealed class QueryService(
    AppDbContext db,
    IServerConnectionFactory connectionFactory,
    ITargetEngineRegistry engineRegistry,
    TimeProvider timeProvider,
    IConfiguration configuration,
    IAccessResolver resolver) : IQueryService
{
    private enum AccessCheck { Ok, NotFound, Forbidden, Blocked }

    private sealed record AccessResult(
        AccessCheck Check,
        Database? Database,
        IReadOnlyList<BlockedColumn>? BlockedColumns,
        string[] TouchedSensitive);

    private async Task<AccessResult> CheckAccessAsync(
        User user, DatabaseId databaseId, string sql, CancellationToken ct)
    {
        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return new AccessResult(AccessCheck.NotFound, null, null, []);
        }

        // Enforce database role: user must have query:execute on this specific database (direct or via group)
        var hasRole = await resolver.HasDatabasePermissionAsync(user.Id, database.Id, Permissions.QueryExecute, ct);
        if (!hasRole)
        {
            return new AccessResult(AccessCheck.Forbidden, null, null, []);
        }

        string[] touchedSensitive = [];

        // ── sensitive column check ────────────────────────────────────────────────
        var sensitiveColumns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == database.Id)
            .ToListAsync(ct);

        if (sensitiveColumns.Count > 0)
        {
            var allSensitive = sensitiveColumns
                .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
                .ToList();
            var allHits = SqlColumnChecker.FindBlockedColumns(sql, allSensitive);

            if (allHits.Count > 0)
            {
                touchedSensitive = allHits
                    .Select(h => $"{h.Schema}.{h.Table}.{h.Column}")
                    .ToArray();

                var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
                var bypassedIds = await db.UserColumnBypasses
                    .AsNoTracking()
                    .Where(b => b.UserId == user.Id && sensitiveColumnIds.Contains(b.SensitiveColumnId))
                    .Select(b => b.SensitiveColumnId)
                    .ToListAsync(ct);

                var blockedColumns = sensitiveColumns
                    .Where(c => !bypassedIds.Contains(c.Id))
                    .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
                    .ToList();

                if (blockedColumns.Count > 0)
                {
                    var blockedHits = SqlColumnChecker.FindBlockedColumns(sql, blockedColumns);
                    if (blockedHits.Count > 0)
                    {
                        var blockedList = blockedHits
                            .Select(h => new BlockedColumn(h.Schema, h.Table, h.Column))
                            .ToList();
                        return new AccessResult(AccessCheck.Blocked, database, blockedList, touchedSensitive);
                    }
                }
            }
        }

        return new AccessResult(AccessCheck.Ok, database, null, touchedSensitive);
    }

    public async Task<QueryExplainResult> ExplainAsync(
        User user, DatabaseId databaseId, string sql, bool analyze, CancellationToken ct)
    {
        var access = await CheckAccessAsync(user, databaseId, sql, ct);
        switch (access.Check)
        {
            case AccessCheck.NotFound:
                return new QueryExplainResult(QueryOutcome.NotFound, null, null, null);
            case AccessCheck.Forbidden:
                return new QueryExplainResult(QueryOutcome.Forbidden, null, null, null);
            case AccessCheck.Blocked:
                return new QueryExplainResult(QueryOutcome.Blocked, null, access.BlockedColumns, null);
        }

        var explainTimeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var explainTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(explainTimeoutSeconds));
        using var explainLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, explainTimeoutCts.Token);

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(access.Database!.Id, CredentialKind.Read, ct);
            var engine = engineRegistry.Resolve(access.Database.Server!.Kind);
            var plan = await engine.ExplainAsync(connectionString, sql, analyze, explainLinkedCts.Token);
            return new QueryExplainResult(QueryOutcome.Ok, plan, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new QueryExplainResult(QueryOutcome.BadRequest, null, null, ex.Message);
        }
    }

    public async Task<QueryExecutionResult> ExecuteAsync(User user, DatabaseId databaseId, string sql, QuerySource source, CancellationToken ct)
    {
        var startedAt = timeProvider.GetUtcNow();

        var access = await CheckAccessAsync(user, databaseId, sql, ct);
        switch (access.Check)
        {
            case AccessCheck.NotFound:
                return new QueryExecutionResult(QueryOutcome.NotFound, null, null, null);
            case AccessCheck.Forbidden:
                return new QueryExecutionResult(QueryOutcome.Forbidden, null, null, null);
            case AccessCheck.Blocked:
            {
                var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
                var logEntry = QueryLog.Create(user.Id, access.Database!.Id, sql,
                    QueryLogStatus.Blocked, startedAt, durationMs, null,
                    $"Sensitive columns: {string.Join(", ", access.BlockedColumns!.Select(c => $"{c.Schema}.{c.Table}.{c.Column}"))}",
                    access.TouchedSensitive,
                    source);
                db.QueryLogs.Add(logEntry);
                await db.SaveChangesAsync(ct);
                return new QueryExecutionResult(QueryOutcome.Blocked, null, access.BlockedColumns, null);
            }
        }

        var database = access.Database!;
        var touchedSensitive = access.TouchedSensitive;

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        QueryEndpoints.QueryResponse response;
        QueryLogStatus logStatus;
        int? rowCount = null;

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(database.Id, CredentialKind.Read, ct);

            var targetEngine = engineRegistry.Resolve(database.Server!.Kind);

            QueryPlanSummary? estimate = null;
            if (configuration.GetValue("Query:AutoExplain", true))
            {
                try
                {
                    var estimatePlan = await targetEngine.ExplainAsync(connectionString, sql, analyze: false, linkedCts.Token);
                    estimate = estimatePlan.Summary;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Advisory estimate is best-effort; never fail the real query over it.
                    _ = ex;
                }
            }

            var data = await targetEngine.ExecuteQueryAsync(connectionString, sql, linkedCts.Token);
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            rowCount = data.Rows.Length;
            logStatus = QueryLogStatus.Success;
            response = new QueryEndpoints.QueryResponse(data.Columns, data.Rows, rowCount.Value, durationMs, null, estimate);
        }
        catch (InvalidOperationException ex)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, ex.Message, null);

            var log = QueryLog.Create(user.Id, database.Id, sql, logStatus, startedAt, durationMs, null, ex.Message, touchedSensitive, source);
            db.QueryLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return new QueryExecutionResult(QueryOutcome.BadRequest, null, null, ex.Message);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Timeout;
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, $"Query timed out after {timeoutSeconds}s.", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, ex.Message, null);
        }

        var queryLog = QueryLog.Create(user.Id, database.Id, sql, logStatus, startedAt, response.DurationMs, rowCount, response.Error, touchedSensitive, source);
        db.QueryLogs.Add(queryLog);
        await db.SaveChangesAsync(ct);

        return new QueryExecutionResult(QueryOutcome.Ok, response, null, null);
    }
}
