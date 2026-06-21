using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Common;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class UpdateEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/update").RequireAuthorization();

        group.MapPost("/", Submit).WithName("SubmitUpdate");
        group.MapGet("/", List).WithName("ListUpdates");
        group.MapGet("/{id}", Get).WithName("GetUpdate");
        group.MapPost("/{id}/approve", Approve).WithName("ApproveUpdate");
        group.MapPost("/{id}/reject", Reject).WithName("RejectUpdate");
        group.MapPost("/{id}/cancel", Cancel).WithName("CancelUpdate");
        group.MapPost("/{id}/execute", Execute).WithName("ExecuteUpdate");
    }

    // ── submit ───────────────────────────────────────────────────────────────

    private static async Task<Results<Created<UpdateRequestDetailResponse>, BadRequest<string>, NotFound, UnauthorizedHttpResult, ForbidHttpResult>> Submit(
        SubmitUpdateRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);

        if (user is null)
        {
            // Should not be possible
            return TypedResults.Unauthorized();
        }

        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == req.DatabaseId, ct);
        if (database is null)
        {
            return TypedResults.NotFound();
        }

        var hasSubmitRole = await resolver.HasDatabasePermissionAsync(user.Id, database.Id, Permissions.UpdateSubmit, ct);
        if (!hasSubmitRole)
        {
            return TypedResults.Forbid();
        }

        if (database.IsDisabled)
        {
            return TypedResults.BadRequest("Server is disabled.");
        }

        if (!database.CanWrite)
        {
            return TypedResults.BadRequest("Server has no write credentials configured.");
        }

        var request = UpdateRequest.Create(
            database.Id,
            req.SqlText,
            req.Reason,
            new Actioned(user.Id, timeProvider.GetUtcNow()),
            req.SourceRequestId);

        db.UpdateRequests.Add(request);
        await db.SaveChangesAsync(ct);

        var created = await LoadDetail(db, request.Id, ct);
        return TypedResults.Created($"/api/update/{request.Id}", ToDetail(created!));
    }

    // ── list ─────────────────────────────────────────────────────────────────

    private static async Task<Ok<ListUpdateRequestsResponse>> List(
        DateTimeOffset? @from,
        DateTimeOffset? to,
        string? databaseId,
        string? status,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);

        // Collect databases where the user has any update permission (submit, approve, or execute)
        var submitIds = await resolver.DatabasesWithPermissionAsync(user!.Id, Permissions.UpdateSubmit, ct);
        var approveIds = await resolver.DatabasesWithPermissionAsync(user!.Id, Permissions.UpdateApprove, ct);
        var executeIds = await resolver.DatabasesWithPermissionAsync(user!.Id, Permissions.UpdateExecute, ct);
        var allowedDatabaseIds = submitIds.Union(approveIds).Union(executeIds).ToList();

        DatabaseId? filterDb = databaseId is not null && Guid.TryParse(databaseId, out var dbGuid)
            ? DatabaseId.From(dbGuid)
            : null;

        UpdateRequestStatus? filterStatus = status is not null
            && Enum.TryParse<UpdateRequestStatus>(status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : null;

        var requests = await db.UpdateRequests
            .Include(r => r.Database)
            .Include(r => r.Submitter)
            .AsNoTracking()
            .Where(r => r.DatabaseId != null && allowedDatabaseIds.Contains(r.DatabaseId.Value))
            .Where(r => @from == null || r.SubmittedAt >= @from)
            .Where(r => to == null || r.SubmittedAt <= to)
            .Where(r => filterDb == null || r.DatabaseId == filterDb)
            .Where(r => filterStatus == null || r.Status == filterStatus)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);

        var items = requests
            .Select(r => new UpdateSummaryItem(
                r.Id,
                r.Database?.DisplayName,
                r.Submitter?.Name ?? r.Submitter?.Email,
                r.Reason,
                r.Status,
                r.SubmittedAt,
                r.ExecSuccess))
            .ToList();

        return TypedResults.Ok(new ListUpdateRequestsResponse(items));
    }

    // ── get ──────────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound>> Get(
        UpdateRequestId id,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);

        var request = await LoadDetail(db, id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request.DatabaseId is not null)
        {
            var hasAnyRole = await resolver.HasAnyDatabasePermissionAsync(
                user!.Id,
                request.DatabaseId.Value,
                [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute],
                ct);
            if (!hasAnyRole)
            {
                return TypedResults.NotFound();
            }
        }

        return TypedResults.Ok(ToDetail(request));
    }

    // ── approve ──────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>, UnauthorizedHttpResult, ForbidHttpResult>> Approve(
        UpdateRequestId id,
        ReviewUpdateRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var request = await LoadForMutation(db, id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        var user = await currentUser.GetAsync(ct);

        if (user is null)
        {
            // Should not be possible
            return TypedResults.Unauthorized();
        }

        var hasApproveRole = await resolver.HasDatabasePermissionAsync(user.Id, request.DatabaseId!.Value, Permissions.UpdateApprove, ct);
        if (!hasApproveRole)
        {
            return TypedResults.Forbid();
        }

        try
        {
            request.Approve(new Actioned(user.Id, timeProvider.GetUtcNow()), req.Note);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDetail((await LoadDetail(db, id, ct))!));
    }

    // ── reject ───────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>, UnauthorizedHttpResult, ForbidHttpResult>> Reject(
        UpdateRequestId id,
        ReviewUpdateRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var request = await LoadForMutation(db, id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        var user = await currentUser.GetAsync(ct);

        if (user is null)
        {
            // Should not be possible
            return TypedResults.Unauthorized();
        }

        var hasApproveRole = await resolver.HasDatabasePermissionAsync(user.Id, request.DatabaseId!.Value, Permissions.UpdateApprove, ct);
        if (!hasApproveRole)
        {
            return TypedResults.Forbid();
        }

        try
        {
            request.Reject(new Actioned(user.Id, timeProvider.GetUtcNow()), req.Note);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDetail((await LoadDetail(db, id, ct))!));
    }

    // ── cancel ───────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>, UnauthorizedHttpResult, ForbidHttpResult>> Cancel(
        UpdateRequestId id,
        CancelUpdateRequest req,
        AppDbContext db,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        CancellationToken ct)
    {
        var request = await LoadForMutation(db, id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        var user = await currentUser.GetAsync(ct);

        if (user is null)
        {
            // Should not be possible
            return TypedResults.Unauthorized();
        }

        var hasSubmitRole = await resolver.HasDatabasePermissionAsync(user.Id, request.DatabaseId!.Value, Permissions.UpdateSubmit, ct);
        if (!hasSubmitRole)
        {
            return TypedResults.Forbid();
        }

        try
        {
            request.Cancel(new Actioned(user.Id, timeProvider.GetUtcNow()), req.Note);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDetail((await LoadDetail(db, id, ct)!)!));
    }

    // ── execute ──────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>, BadRequest<string>, UnauthorizedHttpResult, ForbidHttpResult>> Execute(
        UpdateRequestId id,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        TimeProvider timeProvider,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var request = await LoadForMutation(db, id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (!request.CanExecute())
        {
            return TypedResults.Conflict($"Cannot execute a request in '{request.Status}' state.");
        }

        if (request.DatabaseId is null)
        {
            return TypedResults.Conflict("Server was deleted. Cannot execute.");
        }

        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.DatabaseId, ct);
        if (database is null || !database.CanWrite)
        {
            return TypedResults.Conflict("Server not found or has no write credentials configured.");
        }

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var user = await currentUser.GetAsync(ct);

        if (user is null)
        {
            // Should not be possible
            return TypedResults.Unauthorized();
        }

        var hasExecuteRole = await resolver.HasDatabasePermissionAsync(user.Id, request.DatabaseId!.Value, Permissions.UpdateExecute, ct);
        if (!hasExecuteRole)
        {
            return TypedResults.Forbid();
        }

        var startedAt = timeProvider.GetUtcNow();

        bool success;
        int? affectedRows = null;
        string? execError = null;

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(database.Id, CredentialKind.Write, ct);
            var raw = await targetEngine.ExecuteUpdateAsync(
                connectionString,
                request.SqlText,
                linkedCts.Token);
            affectedRows = raw >= 0 ? raw : null;
            success = true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            success = false;
            execError = $"Execution timed out after {timeoutSeconds}s.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            success = false;
            execError = ex.Message;
        }

        var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
        try
        {
            request.RecordExecution(
                new Actioned(user.Id, timeProvider.GetUtcNow()),
                success,
                durationMs,
                affectedRows,
                execError);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDetail((await LoadDetail(db, id, ct))!));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    // AsNoTracking so that a second call after SaveChangesAsync returns fresh data with nav props.
    private static Task<UpdateRequest?> LoadDetail(AppDbContext db, UpdateRequestId id, CancellationToken ct) =>
        db.UpdateRequests
            .AsNoTracking()
            .Include(r => r.Database)
            .Include(r => r.Submitter)
            .Include(r => r.Reviewer)
            .Include(r => r.Executor)
            .Include(r => r.CancelledBy)
            .SingleOrDefaultAsync(r => r.Id == id, ct);

    // Tracked load for state-transition endpoints that need to mutate the entity.
    private static Task<UpdateRequest?> LoadForMutation(AppDbContext db, UpdateRequestId id, CancellationToken ct) =>
        db.UpdateRequests.SingleOrDefaultAsync(r => r.Id == id, ct);

    private static UpdateRequestDetailResponse ToDetail(UpdateRequest r) =>
        new(r.Id,
            r.DatabaseId,
            r.Database?.DisplayName,
            r.SubmitterId,
            r.Submitter?.Name ?? r.Submitter?.Email,
            r.SqlText,
            r.Reason,
            r.Status,
            r.ReviewerId,
            r.Reviewer?.Name ?? r.Reviewer?.Email,
            r.ReviewNote,
            r.CancelledById,
            r.CancelledBy?.Name ?? r.CancelledBy?.Email,
            r.CancelNote,
            r.ExecutorId,
            r.Executor?.Name ?? r.Executor?.Email,
            r.SubmittedAt,
            r.ReviewedAt,
            r.ExecutedAt,
            r.CancelledAt,
            r.ExecSuccess,
            r.ExecDurationMs,
            r.ExecAffectedRows,
            r.ExecError,
            r.SourceRequestId);

    // ── request / response records ────────────────────────────────────────────

    public sealed record SubmitUpdateRequest(DatabaseId DatabaseId, string SqlText, string Reason, UpdateRequestId? SourceRequestId = null);

    public sealed record ReviewUpdateRequest(string Note);

    public sealed record CancelUpdateRequest(string Note);

    public sealed record UpdateSummaryItem(
        UpdateRequestId Id,
        string? DatabaseDisplayName,
        string? SubmitterName,
        string Reason,
        UpdateRequestStatus Status,
        DateTimeOffset SubmittedAt,
        bool? ExecSuccess);

    public sealed record ListUpdateRequestsResponse(IReadOnlyList<UpdateSummaryItem> Requests);

    public sealed record UpdateRequestDetailResponse(
        UpdateRequestId Id,
        DatabaseId? DatabaseId,
        string? DatabaseDisplayName,
        UserId? SubmitterId,
        string? SubmitterName,
        string SqlText,
        string Reason,
        UpdateRequestStatus Status,
        UserId? ReviewerId,
        string? ReviewerName,
        string? ReviewNote,
        UserId? CancelledById,
        string? CancelledByName,
        string? CancelNote,
        UserId? ExecutorId,
        string? ExecutorName,
        DateTimeOffset SubmittedAt,
        DateTimeOffset? ReviewedAt,
        DateTimeOffset? ExecutedAt,
        DateTimeOffset? CancelledAt,
        bool? ExecSuccess,
        int? ExecDurationMs,
        int? ExecAffectedRows,
        string? ExecError,
        UpdateRequestId? SourceRequestId);
}