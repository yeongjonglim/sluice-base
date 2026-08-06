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
        "statements in your SQL. On success this returns a server-relative 'path' to the request — " +
        "prefix it with this MCP server's base URL (the origin you connect to, without the /mcp " +
        "suffix) and give that clickable link to the user so they can review and approve it.")]
    public static async Task<object> SubmitUpdateRequest(
        [Description("The database id (GUID) from list_databases.")] string databaseId,
        [Description("The write/update SQL to run once a human approves and executes it. Do not wrap it in a " +
            "transaction (no BEGIN/COMMIT/ROLLBACK) — the system already runs it in a managed transaction.")] string sql,
        [Description("Why this change is needed — shown to the human reviewer.")] string reason,
        IUpdateRequestService updates, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(databaseId, out var g)) { throw new ArgumentException("databaseId must be a GUID."); }

        var result = await updates.SubmitAsync(user, DatabaseId.From(g), sql, reason, null, ct);
        return result.Outcome switch
        {
            SubmitOutcome.Ok => BuildSubmitResponse(result.Detail!),
            SubmitOutcome.NotFound => throw new InvalidOperationException("Database not found."),
            SubmitOutcome.Forbidden => throw new InvalidOperationException(
                "You do not have permission to submit update requests for this database."),
            SubmitOutcome.BadRequest => throw new InvalidOperationException(result.Error ?? "Cannot submit update request."),
            _ => throw new InvalidOperationException("Update submit error."),
        };
    }

    // Builds the success payload with a SERVER-RELATIVE path to the request's detail page. We
    // deliberately do not build an absolute URL server-side: the server's own scheme/host is
    // unreliable behind proxies (forwarded headers) and X-Forwarded-Host is client-spoofable. The
    // MCP client already knows the base URL it connected to, so it resolves the path — and the
    // worst case is a harmless relative path rather than a wrong or spoofed absolute link.
    private static object BuildSubmitResponse(UpdateEndpoints.UpdateRequestDetailResponse detail)
    {
        var path = $"/update/{detail.Id.Value}";
        return new
        {
            id = detail.Id,
            status = detail.Status.ToString(),
            path,
            message = "Submitted for human review — you cannot approve or execute it. To give the user a " +
                      "clickable link, prefix this path with this MCP server's base URL (the origin you " +
                      $"connect to, without the /mcp suffix): {path}",
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
