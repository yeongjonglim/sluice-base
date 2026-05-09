using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Endpoints;

internal static class QueryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", ExecuteQuery)
            .RequireAuthorization(Permissions.QueryExecute)
            .WithName("ExecuteQuery");
    }

    private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>>> ExecuteQuery(
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

        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.ServerId, ct);
        if (server is null)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(server.ReadUsername))
        {
            return TypedResults.BadRequest("Server has no read-only credentials configured.");
        }

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        QueryResponse response;
        string logStatus;
        int? rowCount = null;

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(server.Id, CredentialKind.Read, ct);

            var data = await targetEngine.ExecuteQueryAsync(
                connectionString,
                request.Sql,
                linkedCts.Token);

            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            rowCount = data.Rows.Length;
            logStatus = QueryLogStatus.Success;
            response = new QueryResponse(data.Columns, data.Rows, rowCount.Value, durationMs, null);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Timeout;
            response = new QueryResponse(null,
                null,
                0,
                durationMs,
                $"Query timed out after {timeoutSeconds}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryResponse(null, null, 0, durationMs, ex.Message);
        }

        var log = QueryLog.Create(
            userId: user?.Id,
            serverId: server.Id,
            queryText: request.Sql,
            status: logStatus,
            executedAt: startedAt,
            durationMs: response.DurationMs,
            rowCount: rowCount,
            error: response.Error);

        db.QueryLogs.Add(log);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(response);
    }

    public sealed record QueryRequest(ServerId ServerId, string Sql);

    public sealed record QueryResponse(
        string[]? Columns,
        string?[][]? Rows,
        int RowCount,
        int DurationMs,
        string? Error);
}