using System.ComponentModel;
using ModelContextProtocol.Server;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Extensions;
using SluiceBase.Api.Services;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;

namespace SluiceBase.Api.Mcp.Tools;

// Write-side MCP tools. An agent may SUBMIT an update request, read its status, CANCEL a request
// its own user submitted (cancelling only ever prevents a write), and PREVIEW a request, which runs
// its SQL in a transaction that is always rolled back. It can never approve, reject, or execute —
// those transitions stay REST/UI-only and require a human operator. Kept separate from the
// read-only DatabaseTools.
[McpServerToolType]
internal sealed class UpdateTools
{
    [McpServerTool(Name = "submit_update_request", Title = "Submit update request", ReadOnly = true, OpenWorld = false)]
    [Description("Submit a write/update SQL statement for human review. You can submit, and later cancel a " +
        "request you submitted (cancel_update_request), but you cannot approve, reject, or execute it; " +
        "a human operator does that in the app. " +
        "The statement is executed inside a system-managed transaction (rolled back during preview, " +
        "committed on execution), so do NOT include BEGIN/COMMIT/ROLLBACK or any transaction-control " +
        "statements in your SQL. The request can be PREVIEWED — run in the rolled-back transaction, " +
        "returning any result sets it produces — by reviewers and, through preview_update_request, by " +
        "you. Write the SQL so its preview reveals the before/after data change (see the 'sql' argument " +
        "for how to structure it), then preview it once and show the user the result inline as a " +
        "before/after comparison. On success this returns a 'url' to the request's page — give that " +
        "clickable link to the user so they can review and approve it. Preview results are not saved on " +
        "that page, so don't send the user there to see them.")]
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
        IUpdateRequestService updates, ICurrentUserAccessor currentUser, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(databaseId, out var g)) { throw new ArgumentException("databaseId must be a GUID."); }

        var result = await updates.SubmitAsync(user, DatabaseId.From(g), sql, reason, null, ct);
        return result.Outcome switch
        {
            SubmitOutcome.Ok => BuildSubmitResponse(
                result.Detail!, (http.HttpContext ?? throw new InvalidOperationException("No HTTP request.")).Request.BaseUrl()),
            SubmitOutcome.NotFound => throw new InvalidOperationException("Database not found."),
            SubmitOutcome.Forbidden => throw new InvalidOperationException(
                "You do not have permission to submit update requests for this database."),
            SubmitOutcome.BadRequest => throw new InvalidOperationException(result.Error ?? "Cannot submit update request."),
            _ => throw new InvalidOperationException("Update submit error."),
        };
    }

    // The origin of this MCP request is the address the client reached us at, so the link works for
    // the user. A spoofed Host header only changes the link returned to whoever sent it.
    private static object BuildSubmitResponse(UpdateEndpoints.UpdateRequestDetailResponse detail, string baseUrl)
    {
        var url = $"{baseUrl}/update/{detail.Id.Value}";
        return new
        {
            id = detail.Id,
            status = detail.Status.ToString(),
            url,
            message = "Submitted for human review — you cannot approve or execute it, but you can withdraw it " +
                      "with cancel_update_request while it is still Pending or Approved. Call " +
                      "preview_update_request with this id once and show the user its rows inline as a " +
                      "before/after comparison; preview results are not saved on the request's page. " +
                      $"Give the user this link to review and approve it: {url}",
        };
    }

    [McpServerTool(Name = "list_update_requests", Title = "List update requests", ReadOnly = true, OpenWorld = false)]
    [Description("List update requests on databases you can see, newest first. Read-only.")]
    public static async Task<object> ListUpdateRequests(
        [Description("Optional database id (GUID) to filter by.")] string? databaseId,
        [Description("Optional status filter: Pending, Approved, Executing, Rejected, Cancelled, or Executed.")] string? status,
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
                throw new ArgumentException("status must be one of: Pending, Approved, Executing, Rejected, Cancelled, Executed.");
            }
            filterStatus = parsed;
        }

        return await updates.ListAsync(user, from: null, to: null, filterDb, filterStatus, ct);
    }

    [McpServerTool(Name = "get_update_request", Title = "Get update request", ReadOnly = true, OpenWorld = false)]
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

    [McpServerTool(Name = "cancel_update_request", Title = "Cancel update request", ReadOnly = true, OpenWorld = false)]
    [Description("Cancel (withdraw) an update request you submitted so it can never be executed. Only " +
        "requests in the Pending or Approved state can be cancelled, and only by the user who submitted " +
        "them. Cancelling is final — to make the change later, submit a new request. Use this when a " +
        "request is no longer needed or was submitted with a mistake; it does not undo an already " +
        "executed change.")]
    public static async Task<object> CancelUpdateRequest(
        [Description("The update request id (GUID) from submit_update_request or list_update_requests.")] string id,
        [Description("Why the request is being cancelled — recorded on the request and shown to reviewers.")] string note,
        IUpdateRequestService updates, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(id, out var g)) { throw new ArgumentException("id must be a GUID."); }
        if (string.IsNullOrWhiteSpace(note)) { throw new ArgumentException("note is required."); }

        var result = await updates.CancelAsync(user, UpdateRequestId.From(g), note.Trim(), requireSubmitter: true, ct);
        return result.Outcome switch
        {
            CancelUpdateOutcome.Ok => new
            {
                id = result.Detail!.Id,
                status = result.Detail.Status.ToString(),
                message = "Cancelled — this request can no longer be approved or executed.",
            },
            // A Forbidden without a message means no access to the database at all; mask it as
            // not-found, matching get_update_request, so agents can't probe for request ids.
            CancelUpdateOutcome.NotFound => throw new InvalidOperationException("Update request not found."),
            CancelUpdateOutcome.Forbidden => throw new InvalidOperationException(
                result.Error ?? "Update request not found."),
            CancelUpdateOutcome.Conflict => throw new InvalidOperationException(
                $"Cannot cancel this update request: {result.Error}"),
            _ => throw new InvalidOperationException("Update cancel error."),
        };
    }

    [McpServerTool(Name = "preview_update_request", Title = "Preview update request (runs write SQL, rolled back)",
        ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Preview an update request: RUN its write SQL against the database with the write credential " +
        "inside a transaction that is always rolled back, and return the result sets it produces (for example " +
        "the before/after SELECTs and RETURNING rows) and the affected row count. This performs real writes " +
        "until the rollback: the data changes are undone, but sequence increments, NOTIFY, and calls out to " +
        "other systems are not, and it holds row locks while it runs. Allowed only if you submitted the " +
        "request or can approve or execute updates on its database, and only while it is Pending or Approved. " +
        "Each run is recorded on the request's timeline, but its results are NOT saved: the request's page " +
        "cannot show them, so present them to the user here, inline, as a before/after comparison of the " +
        "changed rows (for example a small table with the key and each changed column before and after). " +
        "Run it once per request; don't re-run it to show the same result again or send the user to the " +
        "link to preview it themselves.")]
    public static async Task<object> PreviewUpdateRequest(
        [Description("The update request id (GUID) from submit_update_request or list_update_requests.")] string id,
        IUpdateRequestService updates, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(id, out var g)) { throw new ArgumentException("id must be a GUID."); }

        var result = await updates.PreviewAsync(user, UpdateRequestId.From(g), ct);
        return result.Outcome switch
        {
            PreviewUpdateOutcome.Ok => result.Preview!,
            // Masked like get_update_request, so agents can't probe for request ids they can't see.
            PreviewUpdateOutcome.NotFound or PreviewUpdateOutcome.Forbidden => throw new InvalidOperationException(
                "Update request not found, or you can only preview requests you submitted or can approve or execute."),
            PreviewUpdateOutcome.Conflict => throw new InvalidOperationException(
                $"Cannot preview this update request: {result.Error}"),
            // Same structured guidance as run_query, so a cooperative agent drops the named columns.
            PreviewUpdateOutcome.Blocked => SensitiveColumnBlockPayload.From(
                [.. result.Blocked!.BlockedHits.Select(h => new BlockedColumn(h.Schema, h.Table, h.Column))],
                result.Blocked.PolicyBlockReason),
            _ => throw new InvalidOperationException("Update preview error."),
        };
    }
}
