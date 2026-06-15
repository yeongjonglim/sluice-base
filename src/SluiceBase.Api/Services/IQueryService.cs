using Microsoft.EntityFrameworkCore;
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

internal interface IQueryService
{
    Task<QueryExecutionResult> ExecuteAsync(User user, DatabaseId databaseId, string sql, QuerySource source, CancellationToken ct);
}

internal sealed class QueryService(
    AppDbContext db,
    IServerConnectionFactory connectionFactory,
    ITargetEngine targetEngine,
    TimeProvider timeProvider,
    IConfiguration configuration) : IQueryService
{
    public async Task<QueryExecutionResult> ExecuteAsync(User user, DatabaseId databaseId, string sql, QuerySource source, CancellationToken ct)
    {
        var startedAt = timeProvider.GetUtcNow();

        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return new QueryExecutionResult(QueryOutcome.NotFound, null, null, null);
        }

        // Enforce database role: user must have query:execute on this specific database
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user.Id && r.Permission == Permissions.QueryExecute && r.DatabaseId == database.Id, ct);
        if (!hasRole)
        {
            return new QueryExecutionResult(QueryOutcome.Forbidden, null, null, null);
        }

        // ── sensitive column check ────────────────────────────────────────────────
        var sensitiveColumns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == database.Id)
            .ToListAsync(ct);

        string[] touchedSensitive = [];

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
                        var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
                        var logEntry = QueryLog.Create(user.Id, database.Id, sql,
                            QueryLogStatus.Blocked, startedAt, durationMs, null,
                            $"Sensitive columns: {string.Join(", ", blockedHits.Select(h => $"{h.Schema}.{h.Table}.{h.Column}"))}",
                            touchedSensitive,
                            source);
                        db.QueryLogs.Add(logEntry);
                        await db.SaveChangesAsync(ct);

                        var blockedColumnsList = blockedHits
                            .Select(h => new BlockedColumn(h.Schema, h.Table, h.Column))
                            .ToList();
                        return new QueryExecutionResult(QueryOutcome.Blocked, null, blockedColumnsList, null);
                    }
                }
            }
        }

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

            var data = await targetEngine.ExecuteQueryAsync(connectionString, sql, linkedCts.Token);
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            rowCount = data.Rows.Length;
            logStatus = QueryLogStatus.Success;
            response = new QueryEndpoints.QueryResponse(data.Columns, data.Rows, rowCount.Value, durationMs, null);
        }
        catch (InvalidOperationException ex)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, ex.Message);

            var log = QueryLog.Create(user.Id, database.Id, sql, logStatus, startedAt, durationMs, null, ex.Message, touchedSensitive, source);
            db.QueryLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return new QueryExecutionResult(QueryOutcome.BadRequest, null, null, ex.Message);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Timeout;
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, $"Query timed out after {timeoutSeconds}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryEndpoints.QueryResponse(null, null, 0, durationMs, ex.Message);
        }

        var queryLog = QueryLog.Create(user.Id, database.Id, sql, logStatus, startedAt, response.DurationMs, rowCount, response.Error, touchedSensitive, source);
        db.QueryLogs.Add(queryLog);
        await db.SaveChangesAsync(ct);

        return new QueryExecutionResult(QueryOutcome.Ok, response, null, null);
    }
}
