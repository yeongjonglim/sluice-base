using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Services;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class QueryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", ExecuteQuery)
            .RequireAuthorization()
            .WithName("ExecuteQuery");

        app.MapPost("/api/query/explain", ExplainQuery)
            .RequireAuthorization()
            .WithName("ExplainQuery");

        app.MapGet("/api/query/history", GetHistory)
            .RequireAuthorization()
            .WithName("GetQueryHistory");
    }

    private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>, ForbidHttpResult, ProblemHttpResult>> ExecuteQuery(
        QueryRequest request,
        ICurrentUserAccessor currentUser,
        IQueryService queryService,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var result = await queryService.ExecuteAsync(user!, request.DatabaseId, request.Sql, QuerySource.Ui, ct);

        return result.Outcome switch
        {
            QueryOutcome.NotFound => TypedResults.NotFound(),
            QueryOutcome.Forbidden => TypedResults.Forbid(),
            QueryOutcome.Blocked => TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Sensitive columns",
                type: "sensitive_columns",
                extensions: new Dictionary<string, object?> { ["columns"] = result.BlockedColumns!.Select(c => new { schema = c.Schema, table = c.Table, column = c.Column }).ToArray() }),
            QueryOutcome.BadRequest => TypedResults.BadRequest(result.Error!),
            _ => TypedResults.Ok(result.Response!),
        };
    }

    private static async Task<Results<Ok<QueryPlanResponse>, NotFound, BadRequest<string>, ForbidHttpResult, ProblemHttpResult>> ExplainQuery(
        ExplainRequest request,
        ICurrentUserAccessor currentUser,
        IQueryService queryService,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var result = await queryService.ExplainAsync(user!, request.DatabaseId, request.Sql, request.Analyze, ct);

        return result.Outcome switch
        {
            QueryOutcome.NotFound => TypedResults.NotFound(),
            QueryOutcome.Forbidden => TypedResults.Forbid(),
            QueryOutcome.Blocked => TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Sensitive columns",
                type: "sensitive_columns",
                extensions: new Dictionary<string, object?> { ["columns"] = result.BlockedColumns!.Select(c => new { schema = c.Schema, table = c.Table, column = c.Column }).ToArray() }),
            QueryOutcome.BadRequest => TypedResults.BadRequest(result.Error!),
            _ => TypedResults.Ok(new QueryPlanResponse(result.Plan!.PlanJson, result.Plan.Summary)),
        };
    }

    private static async Task<Results<Ok<QueryHistoryResponse>, BadRequest<string>>> GetHistory(
        DateTimeOffset? @from,
        DateTimeOffset? to,
        string? databaseId,
        string? status,
        string? source,
        [FromQuery] string[]? sensitiveColumn,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        CancellationToken ct)
    {
        if (@from.HasValue && to.HasValue && @from > to)
        {
            return TypedResults.BadRequest("'from' must be before 'to'.");
        }

        var user = await currentUser.GetAsync(ct);

        // databases where user has query:audit (can see all queries, direct or via group)
        var auditDatabaseIds = await resolver.DatabasesWithPermissionAsync(user!.Id, Permissions.QueryAudit, ct);

        // databases where user has any scopeable role (can see own queries, direct or via group)
        var anyRoleDatabaseIds = await resolver.DatabasesWithAnyScopeableAsync(user!.Id, ct);

        DatabaseId? filterDb = databaseId is not null && Guid.TryParse(databaseId, out var dbGuid)
            ? DatabaseId.From(dbGuid)
            : null;

        QueryLogStatus? filterStatus = status is not null
            && Enum.TryParse<QueryLogStatus>(status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : null;

        QuerySource? filterSource = source is not null
            && Enum.TryParse<QuerySource>(source, ignoreCase: true, out var parsedSource)
            ? parsedSource
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
            .Where(q => filterStatus == null || q.Status == filterStatus)
            .Where(q => filterSource == null || q.Source == filterSource);

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
                (from d in db.Databases
                 join s in db.Servers on d.ServerId equals s.Id
                 where d.Id == q.DatabaseId
                 select s.Name).FirstOrDefault(),
                q.QueryText,
                q.Status,
                q.ExecutedAt,
                q.DurationMs,
                q.RowCount,
                q.Error,
                q.UserId,
                db.Users.Where(u => u.Id == q.UserId).Select(u => u.Name ?? u.Email).FirstOrDefault(),
                q.SensitiveColumns,
                q.Source
            ))
            .ToListAsync(ct);

        return TypedResults.Ok(new QueryHistoryResponse(items));
    }

    public sealed record QueryRequest(DatabaseId DatabaseId, string Sql);

    public sealed record ExplainRequest(DatabaseId DatabaseId, string Sql, bool Analyze);

    public sealed record QueryPlanResponse(string PlanJson, QueryPlanSummary Summary);

    public sealed record QueryResponse(
        string[]? Columns,
        string?[][]? Rows,
        int RowCount,
        int DurationMs,
        string? Error,
        QueryPlanSummary? Estimate);

    public sealed record QueryHistoryItem(
        QueryLogId Id,
        DatabaseId? DatabaseId,
        string? DatabaseDisplayName,
        string? ServerName,
        string QueryText,
        QueryLogStatus Status,
        DateTimeOffset ExecutedAt,
        int? DurationMs,
        int? RowCount,
        string? Error,
        UserId? UserId,
        string? UserName,
        string[] SensitiveColumns,
        QuerySource Source);

    public sealed record QueryHistoryResponse(IReadOnlyList<QueryHistoryItem> Items);
}
