using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
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

    private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>, ForbidHttpResult>> ExecuteQuery(
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

            var log = QueryLog.Create(user?.Id, database.Id, request.Sql, logStatus, startedAt, durationMs, null, ex.Message);
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

        var queryLog = QueryLog.Create(user?.Id, database.Id, request.Sql, logStatus, startedAt, response.DurationMs, rowCount, response.Error);
        db.QueryLogs.Add(queryLog);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<QueryHistoryResponse>, BadRequest<string>>> GetHistory(
        DateTimeOffset? @from,
        DateTimeOffset? to,
        string? databaseId,
        string? status,
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

        var items = await db.QueryLogs
            .AsNoTracking()
            .Where(q => q.DatabaseId != null &&
                        (auditDatabaseIds.Contains(q.DatabaseId.Value) ||
                         (anyRoleDatabaseIds.Contains(q.DatabaseId.Value) && q.UserId == user!.Id)))
            .Where(q => @from == null || q.ExecutedAt >= @from)
            .Where(q => to == null || q.ExecutedAt <= to)
            .Where(q => filterDb == null || q.DatabaseId == filterDb)
            .Where(q => filterStatus == null || q.Status == filterStatus)
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
                db.Users.Where(u => u.Id == q.UserId).Select(u => u.Name ?? u.Email).FirstOrDefault()
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
        string? UserName);

    public sealed record QueryHistoryResponse(IReadOnlyList<QueryHistoryItem> Items);
}
