using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Queries;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class QueryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", ExecuteQuery)
            .RequireAuthorization()
            .WithName("ExecuteQuery");

        app.MapGet("/api/query/history", GetHistory)
            .RequireAuthorization()
            .WithName("GetQueryHistory");
    }

    private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>, ForbidHttpResult, ProblemHttpResult>> ExecuteQuery(
        QueryRequest request,
        AppDbContext db,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var startedAt = timeProvider.GetUtcNow();

        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == request.DatabaseId, ct);
        if (database is null)
        {
            return TypedResults.NotFound();
        }

        // Enforce database role: user must have query:execute on this specific database
        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.QueryExecute && r.DatabaseId == database.Id, ct);
        if (!hasRole)
        {
            return TypedResults.Forbid();
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
            var allHits = SqlColumnChecker.FindBlockedColumns(request.Sql, allSensitive);

            if (allHits.Count > 0)
            {
                touchedSensitive = allHits
                    .Select(h => $"{h.Schema}.{h.Table}.{h.Column}")
                    .ToArray();

                var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
                var bypassedIds = await db.UserColumnBypasses
                    .AsNoTracking()
                    .Where(b => b.UserId == user!.Id && sensitiveColumnIds.Contains(b.SensitiveColumnId))
                    .Select(b => b.SensitiveColumnId)
                    .ToListAsync(ct);

                var blockedColumns = sensitiveColumns
                    .Where(c => !bypassedIds.Contains(c.Id))
                    .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
                    .ToList();

                if (blockedColumns.Count > 0)
                {
                    var blockedHits = SqlColumnChecker.FindBlockedColumns(request.Sql, blockedColumns);

                    if (blockedHits.Count > 0)
                    {
                        var blocked = blockedHits.Select(h => new { schema = h.Schema, table = h.Table, column = h.Column }).ToArray();
                        var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
                        var logEntry = QueryLog.Create(user?.Id, database.Id, request.Sql,
                            QueryLogStatus.Blocked, startedAt, durationMs, null,
                            $"Sensitive columns: {string.Join(", ", blockedHits.Select(h => $"{h.Schema}.{h.Table}.{h.Column}"))}",
                            touchedSensitive);
                        db.QueryLogs.Add(logEntry);
                        await db.SaveChangesAsync(ct);

                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status403Forbidden,
                            title: "Sensitive columns",
                            type: "sensitive_columns",
                            extensions: new Dictionary<string, object?> { ["columns"] = blocked });
                    }
                }
            }
        }

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        QueryResponse response;
        QueryLogStatus logStatus;
        int? rowCount = null;

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(database.Id, CredentialKind.Read, ct);

            var data = await targetEngine.ExecuteQueryAsync(connectionString, request.Sql, linkedCts.Token);
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            rowCount = data.Rows.Length;
            logStatus = QueryLogStatus.Success;
            response = new QueryResponse(data.Columns, data.Rows, rowCount.Value, durationMs, null);
        }
        catch (InvalidOperationException ex)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryResponse(null, null, 0, durationMs, ex.Message);

            var log = QueryLog.Create(user?.Id, database.Id, request.Sql, logStatus, startedAt, durationMs, null, ex.Message, touchedSensitive);
            db.QueryLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return TypedResults.BadRequest(ex.Message);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Timeout;
            response = new QueryResponse(null, null, 0, durationMs, $"Query timed out after {timeoutSeconds}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryResponse(null, null, 0, durationMs, ex.Message);
        }

        var queryLog = QueryLog.Create(user?.Id, database.Id, request.Sql, logStatus, startedAt, response.DurationMs, rowCount, response.Error, touchedSensitive);
        db.QueryLogs.Add(queryLog);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<QueryHistoryResponse>, BadRequest<string>>> GetHistory(
        DateTimeOffset? @from,
        DateTimeOffset? to,
        string? databaseId,
        string? status,
        [FromQuery] string[]? sensitiveColumn,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        if (@from.HasValue && to.HasValue && @from > to)
        {
            return TypedResults.BadRequest("'from' must be before 'to'.");
        }

        var user = await currentUser.GetAsync(ct);

        // databases where user has query:audit (can see all queries)
        var auditDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id && r.Permission == Permissions.QueryAudit)
            .Select(r => r.DatabaseId)
            .ToListAsync(ct);

        // databases where user has any role (can see own queries)
        var anyRoleDatabaseIds = await db.UserDatabaseRoles
            .Where(r => r.UserId == user!.Id)
            .Select(r => r.DatabaseId)
            .Distinct()
            .ToListAsync(ct);

        DatabaseId? filterDb = databaseId is not null && Guid.TryParse(databaseId, out var dbGuid)
            ? DatabaseId.From(dbGuid)
            : null;

        QueryLogStatus? filterStatus = status is not null
            && Enum.TryParse<QueryLogStatus>(status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : null;

        var hasSensitiveFilter = sensitiveColumn is { Length: > 0 };
        var sensitiveFilterAny = hasSensitiveFilter && sensitiveColumn!.Length == 1
            && string.Equals(sensitiveColumn[0], "any", StringComparison.OrdinalIgnoreCase);

        var query = db.QueryLogs
            .AsNoTracking()
            .Where(q => q.DatabaseId != null &&
                        (auditDatabaseIds.Contains(q.DatabaseId.Value) ||
                         (anyRoleDatabaseIds.Contains(q.DatabaseId.Value) && q.UserId == user!.Id)))
            .Where(q => @from == null || q.ExecutedAt >= @from)
            .Where(q => to == null || q.ExecutedAt <= to)
            .Where(q => filterDb == null || q.DatabaseId == filterDb)
            .Where(q => filterStatus == null || q.Status == filterStatus);

        if (sensitiveFilterAny)
        {
            query = query.Where(q => q.SensitiveColumns.Length > 0);
        }
        else if (hasSensitiveFilter)
        {
            query = query.Where(q => q.SensitiveColumns.Any(sc => sensitiveColumn!.Contains(sc)));
        }

        var items = await query
            .OrderByDescending(q => q.ExecutedAt)
            .Take(100)
            .Select(q => new QueryHistoryItem(
                q.Id,
                q.DatabaseId,
                db.Databases.Where(d => d.Id == q.DatabaseId).Select(d => d.DisplayName).FirstOrDefault(),
                q.QueryText,
                q.Status,
                q.ExecutedAt,
                q.DurationMs,
                q.RowCount,
                q.Error,
                q.UserId,
                db.Users.Where(u => u.Id == q.UserId).Select(u => u.Name ?? u.Email).FirstOrDefault(),
                q.SensitiveColumns
            ))
            .ToListAsync(ct);

        return TypedResults.Ok(new QueryHistoryResponse(items));
    }

    public sealed record QueryRequest(DatabaseId DatabaseId, string Sql);

    public sealed record QueryResponse(
        string[]? Columns,
        string?[][]? Rows,
        int RowCount,
        int DurationMs,
        string? Error);

    public sealed record QueryHistoryItem(
        QueryLogId Id,
        DatabaseId? DatabaseId,
        string? DatabaseDisplayName,
        string QueryText,
        QueryLogStatus Status,
        DateTimeOffset ExecutedAt,
        int? DurationMs,
        int? RowCount,
        string? Error,
        UserId? UserId,
        string? UserName,
        string[] SensitiveColumns);

    public sealed record QueryHistoryResponse(IReadOnlyList<QueryHistoryItem> Items);
}
