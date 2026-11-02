using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Queries;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Common;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Services;

internal enum SubmitOutcome { Ok, NotFound, Forbidden, BadRequest }

internal sealed record SubmitUpdateResult(
    SubmitOutcome Outcome,
    UpdateEndpoints.UpdateRequestDetailResponse? Detail,
    string? Error);

internal enum GetUpdateOutcome { Ok, NotFound }

internal sealed record GetUpdateResult(
    GetUpdateOutcome Outcome,
    UpdateEndpoints.UpdateRequestDetailResponse? Detail);

internal enum CancelUpdateOutcome { Ok, NotFound, Forbidden, Conflict }

internal sealed record CancelUpdateResult(
    CancelUpdateOutcome Outcome,
    UpdateEndpoints.UpdateRequestDetailResponse? Detail,
    string? Error);

internal enum PreviewUpdateOutcome { Ok, NotFound, Forbidden, Conflict, Blocked }

internal sealed record PreviewUpdateResult(
    PreviewUpdateOutcome Outcome,
    UpdateEndpoints.UpdatePreviewResponse? Preview,
    string? Error,
    SensitiveColumnDecision? Blocked);

// Shared submit/list/get/cancel/preview logic for update requests, called by both the REST
// endpoints (UpdateEndpoints) and the MCP tools (UpdateTools) so the two surfaces cannot drift.
// The remaining mutation transitions (approve/reject/execute) are NOT here — they remain
// REST/UI-only and are never exposed over MCP.
internal interface IUpdateRequestService
{
    Task<SubmitUpdateResult> SubmitAsync(
        User user, DatabaseId databaseId, string sqlText, string reason,
        UpdateRequestId? sourceRequestId, CancellationToken ct);

    Task<UpdateEndpoints.ListUpdateRequestsResponse> ListAsync(
        User user, DateTimeOffset? from, DateTimeOffset? to,
        DatabaseId? databaseId, UpdateRequestStatus? status, CancellationToken ct);

    Task<GetUpdateResult> GetAsync(User user, UpdateRequestId id, CancellationToken ct);

    // requireSubmitter limits the cancel to requests the user submitted themselves (MCP), on top
    // of the update:submit permission that REST already requires.
    Task<CancelUpdateResult> CancelAsync(
        User user, UpdateRequestId id, string note, bool requireSubmitter, CancellationToken ct);

    // Runs the request's SQL with the write credential in a transaction that is always rolled back.
    // Allowed for the submitter or anyone who can approve or execute on the database.
    Task<PreviewUpdateResult> PreviewAsync(User user, UpdateRequestId id, CancellationToken ct);
}

internal sealed class UpdateRequestService(
    AppDbContext db,
    IAccessResolver resolver,
    TimeProvider timeProvider,
    IServerConnectionFactory connectionFactory,
    ITargetEngineRegistry engineRegistry,
    SensitiveColumnGuard sensitiveGuard,
    IConfiguration configuration) : IUpdateRequestService
{
    public async Task<SubmitUpdateResult> SubmitAsync(
        User user, DatabaseId databaseId, string sqlText, string reason,
        UpdateRequestId? sourceRequestId, CancellationToken ct)
    {
        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return new SubmitUpdateResult(SubmitOutcome.NotFound, null, null);
        }

        var hasSubmitRole = await resolver.HasDatabasePermissionAsync(user.Id, database.Id, Permissions.UpdateSubmit, ct);
        if (!hasSubmitRole)
        {
            return new SubmitUpdateResult(SubmitOutcome.Forbidden, null, null);
        }

        if (database.IsDisabled)
        {
            return new SubmitUpdateResult(SubmitOutcome.BadRequest, null, "Server is disabled.");
        }

        if (!database.CanWrite)
        {
            return new SubmitUpdateResult(SubmitOutcome.BadRequest, null, "Server has no write credentials configured.");
        }

        var request = UpdateRequest.Create(
            database.Id,
            sqlText,
            reason,
            new Actioned(user.Id, timeProvider.GetUtcNow()),
            sourceRequestId);

        db.UpdateRequests.Add(request);
        await db.SaveChangesAsync(ct);

        var created = await UpdateEndpoints.LoadDetail(db, request.Id, ct);
        return new SubmitUpdateResult(SubmitOutcome.Ok, UpdateEndpoints.ToDetail(created!), null);
    }

    public async Task<UpdateEndpoints.ListUpdateRequestsResponse> ListAsync(
        User user, DateTimeOffset? from, DateTimeOffset? to,
        DatabaseId? databaseId, UpdateRequestStatus? status, CancellationToken ct)
    {
        // Collect databases where the user has any update permission (submit, approve, or execute)
        var submitIds = await resolver.DatabasesWithPermissionAsync(user.Id, Permissions.UpdateSubmit, ct);
        var approveIds = await resolver.DatabasesWithPermissionAsync(user.Id, Permissions.UpdateApprove, ct);
        var executeIds = await resolver.DatabasesWithPermissionAsync(user.Id, Permissions.UpdateExecute, ct);
        var allowedDatabaseIds = submitIds.Union(approveIds).Union(executeIds).ToList();

        var requests = await db.UpdateRequests
            .Include(r => r.Database)
            .Include(r => r.Submitter)
            .AsNoTracking()
            .Where(r => r.DatabaseId != null && allowedDatabaseIds.Contains(r.DatabaseId.Value))
            .Where(r => from == null || r.SubmittedAt >= from)
            .Where(r => to == null || r.SubmittedAt <= to)
            .Where(r => databaseId == null || r.DatabaseId == databaseId)
            .Where(r => status == null || r.Status == status)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);

        var items = requests.Select(UpdateEndpoints.ToSummary).ToList();
        return new UpdateEndpoints.ListUpdateRequestsResponse(items);
    }

    public async Task<GetUpdateResult> GetAsync(User user, UpdateRequestId id, CancellationToken ct)
    {
        var request = await UpdateEndpoints.LoadDetail(db, id, ct);
        if (request is null)
        {
            return new GetUpdateResult(GetUpdateOutcome.NotFound, null);
        }

        if (request.DatabaseId is not null)
        {
            var hasAnyRole = await resolver.HasAnyDatabasePermissionAsync(
                user.Id,
                request.DatabaseId.Value,
                [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute],
                ct);
            if (!hasAnyRole)
            {
                // Mask as not-found so callers can't probe for requests they can't see.
                return new GetUpdateResult(GetUpdateOutcome.NotFound, null);
            }
        }

        return new GetUpdateResult(GetUpdateOutcome.Ok, UpdateEndpoints.ToDetail(request));
    }

    public async Task<CancelUpdateResult> CancelAsync(
        User user, UpdateRequestId id, string note, bool requireSubmitter, CancellationToken ct)
    {
        var request = await db.UpdateRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return new CancelUpdateResult(CancelUpdateOutcome.NotFound, null, null);
        }

        if (request.DatabaseId is null)
        {
            return new CancelUpdateResult(CancelUpdateOutcome.Forbidden, null, null);
        }

        var hasSubmitRole = await resolver.HasDatabasePermissionAsync(user.Id, request.DatabaseId.Value, Permissions.UpdateSubmit, ct);
        if (!hasSubmitRole)
        {
            return new CancelUpdateResult(CancelUpdateOutcome.Forbidden, null, null);
        }

        if (requireSubmitter && request.SubmitterId != user.Id)
        {
            return new CancelUpdateResult(
                CancelUpdateOutcome.Forbidden, null, "You can only cancel update requests you submitted.");
        }

        try
        {
            request.Cancel(new Actioned(user.Id, timeProvider.GetUtcNow()), note);
        }
        catch (InvalidOperationException ex)
        {
            return new CancelUpdateResult(CancelUpdateOutcome.Conflict, null, ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var cancelled = await UpdateEndpoints.LoadDetail(db, id, ct);
        return new CancelUpdateResult(CancelUpdateOutcome.Ok, UpdateEndpoints.ToDetail(cancelled!), null);
    }

    public async Task<PreviewUpdateResult> PreviewAsync(User user, UpdateRequestId id, CancellationToken ct)
    {
        var request = await db.UpdateRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return new PreviewUpdateResult(PreviewUpdateOutcome.NotFound, null, null, null);
        }

        if (request.Status is not (UpdateRequestStatus.Pending or UpdateRequestStatus.Approved))
        {
            return new PreviewUpdateResult(PreviewUpdateOutcome.Conflict, null, $"Cannot preview a request in '{request.Status}' state.", null);
        }

        if (request.DatabaseId is null)
        {
            return new PreviewUpdateResult(PreviewUpdateOutcome.Conflict, null, "Server was deleted. Cannot preview.", null);
        }

        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(s => s.Id == request.DatabaseId, ct);
        if (database is null || !database.CanWrite)
        {
            return new PreviewUpdateResult(PreviewUpdateOutcome.Conflict, null, "Server not found or has no write credentials configured.", null);
        }

        if (database.IsDisabled)
        {
            return new PreviewUpdateResult(PreviewUpdateOutcome.Conflict, null, "Server is disabled.", null);
        }

        // Submitter of this request, or anyone who can approve/execute on the db.
        var isSubmitter = request.SubmitterId == user.Id;
        var canApprove = await resolver.HasDatabasePermissionAsync(user.Id, request.DatabaseId.Value, Permissions.UpdateApprove, ct);
        var canExecute = await resolver.HasDatabasePermissionAsync(user.Id, request.DatabaseId.Value, Permissions.UpdateExecute, ct);
        if (!isSubmitter && !canApprove && !canExecute)
        {
            return new PreviewUpdateResult(PreviewUpdateOutcome.Forbidden, null, null, null);
        }

        // Sensitive-column gate — same policy as the read path. A hit blocks the run
        // entirely; the SQL never executes and no event is recorded.
        var decision = await sensitiveGuard.EvaluateAsync(user.Id, request.DatabaseId.Value, request.SqlText, ct);
        if (decision.BlockedHits.Count > 0 || decision.PolicyBlockReason is not null)
        {
            return new PreviewUpdateResult(PreviewUpdateOutcome.Blocked, null, null, decision);
        }

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var startedAt = timeProvider.GetUtcNow();
        IReadOnlyList<QueryData> resultSets = [];
        var affectedRows = 0;
        bool success;
        string? error = null;

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(database.Id, CredentialKind.Write, ct);
            var engine = engineRegistry.Resolve(database.Server!.Kind);
            var result = await engine.ExecuteUpdateAsync(connectionString, request.SqlText, commit: false, linkedCts.Token);
            resultSets = result.ResultSets;
            affectedRows = result.AffectedRows;
            success = true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            success = false;
            error = $"Preview timed out after {timeoutSeconds}s.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            success = false;
            error = ex.Message;
        }

        var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;

        db.UpdateRequestEvents.Add(UpdateRequestEvent.Preview(
            request.Id,
            new Actioned(user.Id, timeProvider.GetUtcNow()),
            success,
            durationMs,
            affectedRows,
            resultSets.Count,
            error));
        await db.SaveChangesAsync(ct);

        return new PreviewUpdateResult(
            PreviewUpdateOutcome.Ok, new UpdateEndpoints.UpdatePreviewResponse(resultSets, affectedRows, durationMs, error), null, null);
    }
}
