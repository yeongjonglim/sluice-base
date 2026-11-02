using System.ComponentModel;
using ModelContextProtocol.Server;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Services;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;

namespace SluiceBase.Api.Mcp.Tools;

// Write-side MCP tools. An agent may SUBMIT an update request and read its status,
// but can never approve, reject, cancel, or execute — those transitions stay REST/UI-only
// and require a human operator. Kept separate from the read-only DatabaseTools.
[McpServerToolType]
internal sealed class UpdateTools
{
    [McpServerTool(Name = "submit_update_request")]
    [Description("Submit a write/update SQL statement for human review. You can ONLY submit — you cannot " +
        "approve, reject, cancel, or execute the request; a human operator does that in the app. " +
        "The statement is executed inside a system-managed transaction (rolled back during preview, " +
        "committed on execution), so do NOT include BEGIN/COMMIT/ROLLBACK or any transaction-control " +
        "statements in your SQL. Before approving, a reviewer can PREVIEW this SQL — it runs in the " +
        "rolled-back transaction and returns any result sets it produces — so write the SQL so its " +
        "preview reveals the before/after data change (see the 'sql' argument for how to structure it). " +
        "On success this returns a 'url' to the request's page — give that clickable link to the " +
        "user so they can review and approve it.")]
    public static async Task<object> SubmitUpdateRequest(
        [Description("The database id (GUID) from list_databases.")] string databaseId,
        [Description("The write/update SQL to run once a human approves and executes it. Do not wrap it in a " +
            "transaction (no BEGIN/COMMIT/ROLLBACK) — the system already runs it in a managed transaction. " +
            "Because reviewers can PREVIEW this SQL (executed then rolled back, returning its result sets), " +
            "include a before/after comparison so the change is visible in the preview: a leading SELECT of the " +
            "rows you will touch (the 'before'), then the mutation with a RETURNING clause — or, on engines " +
            "without RETURNING, a trailing SELECT of the same rows — to show the 'after'. Keep the comparison to " +
            "the key and changed columns, and leave out sensitive columns (preview blocks them just like reads do).")] string sql,
        [Description("Why this change is needed — shown to the human reviewer. If your SQL includes a " +
            "before/after comparison, briefly note what the reviewer should look for in its preview result sets.")] string reason,
        IUpdateRequestService updates, ICurrentUserAccessor currentUser, IMcpIssuer issuer, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(databaseId, out var g)) { throw new ArgumentException("databaseId must be a GUID."); }

        var result = await updates.SubmitAsync(user, DatabaseId.From(g), sql, reason, null, ct);
        return result.Outcome switch
        {
            SubmitOutcome.Ok => BuildSubmitResponse(result.Detail!, issuer.BaseUrl()),
            SubmitOutcome.NotFound => throw new InvalidOperationException("Database not found."),
            SubmitOutcome.Forbidden => throw new InvalidOperationException(
                "You do not have permission to submit update requests for this database."),
            SubmitOutcome.BadRequest => throw new InvalidOperationException(result.Error ?? "Cannot submit update request."),
            _ => throw new InvalidOperationException("Update submit error."),
        };
    }

    // Same origin the OAuth metadata advertises, so the link matches the server the client signed in to.
    private static object BuildSubmitResponse(UpdateEndpoints.UpdateRequestDetailResponse detail, string baseUrl)
    {
        var url = $"{baseUrl}/update/{detail.Id.Value}";
        return new
        {
            id = detail.Id,
            status = detail.Status.ToString(),
            url,
            message = "Submitted for human review — you cannot approve or execute it. Give the user this " +
                      $"link to review it: {url}",
        };
    }

    [McpServerTool(Name = "list_update_requests")]
    [Description("List update requests on databases you can see, newest first. Read-only.")]
    public static async Task<object> ListUpdateRequests(
        [Description("Optional database id (GUID) to filter by.")] string? databaseId,
        [Description("Optional status filter: Pending, Approved, Rejected, Cancelled, or Executed.")] string? status,
        IUpdateRequestService updates, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");

        DatabaseId? filterDb = null;
        if (!string.IsNullOrWhiteSpace(databaseId))
        {
            if (!Guid.TryParse(databaseId, out var g)) { throw new ArgumentException("databaseId must be a GUID."); }
            filterDb = DatabaseId.From(g);
        }

        UpdateRequestStatus? filterStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<UpdateRequestStatus>(status, ignoreCase: true, out var parsed))
            {
                throw new ArgumentException("status must be one of: Pending, Approved, Rejected, Cancelled, Executed.");
            }
            filterStatus = parsed;
        }

        return await updates.ListAsync(user, from: null, to: null, filterDb, filterStatus, ct);
    }

    [McpServerTool(Name = "get_update_request")]
    [Description("Get the full detail and event history of a single update request. Read-only.")]
    public static async Task<object> GetUpdateRequest(
        [Description("The update request id (GUID) from submit_update_request or list_update_requests.")] string id,
        IUpdateRequestService updates, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(id, out var g)) { throw new ArgumentException("id must be a GUID."); }

        var result = await updates.GetAsync(user, UpdateRequestId.From(g), ct);
        return result.Outcome switch
        {
            GetUpdateOutcome.Ok => result.Detail!,
            _ => throw new InvalidOperationException("Update request not found."),
        };
    }
}
