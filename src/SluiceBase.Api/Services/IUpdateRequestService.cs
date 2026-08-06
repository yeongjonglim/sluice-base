using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Common;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
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

// Shared submit/list/get logic for update requests, called by both the REST endpoints
// (UpdateEndpoints) and the MCP tools (UpdateTools) so the two surfaces cannot drift.
// Mutation transitions (approve/reject/cancel/execute/preview) are NOT here — they remain
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
}

internal sealed class UpdateRequestService(
    AppDbContext db,
    IAccessResolver resolver,
    TimeProvider timeProvider) : IUpdateRequestService
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
}
