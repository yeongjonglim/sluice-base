using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Updates;

namespace IntegrationTests;

public class UpdateEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }

    // Sets up Alice with all update permissions and returns the blue server's ID
    private async Task<(AuthenticatedSession session, string serverId)> AliceWithBlueServerAsync(
        string[] permissions,
        CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        foreach (var perm in permissions)
        {
            using var grant = MutationRequest(HttpMethod.Post,
                $"/api/admin/user/{alice.Id}/permission",
                xsrf, new { permission = perm });
            (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();
        }

        // Register blue server with write credentials
        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"upd-{Guid.NewGuid():N}"[..24];
        using var createReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(
                serverName,
                "postgres",
                blueBuilder.Host!,
                blueBuilder.Port,
                "appdb",
                "reader_blue",
                "reader_blue",
                "writer_blue",
                "writer_blue"));

        // Need server:manage permission for server creation; grant it temporarily
        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var createResp = await session.Client.SendAsync(createReq, ct);
        createResp.EnsureSuccessStatusCode();
        var server = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

        return (session, server!.Id.Value.ToString());
    }

    // ── auth guards ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostUpdate_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/update");
        req.Content = JsonContent.Create(new { serverId = Guid.NewGuid(), sqlText = "UPDATE x", reason = "r" });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PostUpdate_Returns403_ForUserWithoutPermission()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        using var req = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { serverId = Guid.NewGuid(), sqlText = "UPDATE x", reason = "r" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetUpdates_Returns403_ForUserWithoutAnyUpdatePermission()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        var resp = await session.Client.GetAsync("/api/update", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── submit ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostUpdate_Returns201_WithPendingStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var req = MutationRequest(HttpMethod.Post, "/api/update", xsrf, new
        {
            serverId,
            sqlText = "UPDATE public.users SET email = email WHERE 1=0",
            reason = "test submission",
        });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var detail = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        Assert.NotNull(detail);
        Assert.Equal(UpdateRequestStatus.Pending, detail.Status);
        Assert.Equal("test submission", detail.Reason);
        Assert.Null(detail.ReviewNote);
    }

    // ── list and get ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUpdates_ReturnsList_ForUserWithSubmitPermission()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        // Submit one first
        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf, new
        {
            serverId,
            sqlText = "UPDATE public.users SET email = email WHERE 1=0",
            reason = "list test",
        });
        (await session.Client.SendAsync(submitReq, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetAsync("/api/update", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var list = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(ct);
        Assert.NotNull(list);
        Assert.NotEmpty(list.Requests);
    }

    // ── full happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task FullFlow_Pending_Approved_Executed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        // Submit
        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf, new
        {
            serverId,
            sqlText = "UPDATE public.users SET email = email WHERE 1=0",
            reason = "happy path test",
        });
        var submitResp = await session.Client.SendAsync(submitReq, ct);
        submitResp.EnsureSuccessStatusCode();
        var submitted = await submitResp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        Assert.Equal(UpdateRequestStatus.Pending, submitted!.Status);
        var requestId = submitted.Id;

        // Approve
        using var approveReq = MutationRequest(HttpMethod.Post,
            $"/api/update/{requestId}/approve", xsrf, new { note = "looks good" });
        var approveResp = await session.Client.SendAsync(approveReq, ct);
        approveResp.EnsureSuccessStatusCode();
        var approved = await approveResp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        Assert.Equal(UpdateRequestStatus.Approved, approved!.Status);
        Assert.Equal("looks good", approved.ReviewNote);

        // Execute
        using var executeReq = MutationRequest(HttpMethod.Post,
            $"/api/update/{requestId}/execute", xsrf);
        var executeResp = await session.Client.SendAsync(executeReq, ct);
        executeResp.EnsureSuccessStatusCode();
        var executed = await executeResp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        Assert.Equal(UpdateRequestStatus.Executed, executed!.Status);
        Assert.True(executed.ExecSuccess);
        Assert.NotNull(executed.ExecDurationMs);
        Assert.Null(executed.ExecError);
    }

    // ── state machine guards ─────────────────────────────────────────────────

    [Fact]
    public async Task Approve_Returns409_WhenAlreadyExecuted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        // Submit → Approve → Execute
        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        var id = submitted!.Id;

        using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
        (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();
        using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
        (await session.Client.SendAsync(er, ct)).EnsureSuccessStatusCode();

        // Try to approve again after execution
        using var ar2 = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "again" });
        var resp = await session.Client.SendAsync(ar2, ct);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Execute_Returns409_WhenStillPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateExecute], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        var id = submitted!.Id;

        using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
        var resp = await session.Client.SendAsync(er, ct);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns409_WhenAlreadyRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        var id = submitted!.Id;

        using var rr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/reject", xsrf, new { note = "no" });
        (await session.Client.SendAsync(rr, ct)).EnsureSuccessStatusCode();

        using var cr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/cancel", xsrf, new { note = "not required anymore"});
        var resp = await session.Client.SendAsync(cr, ct);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns200_WhenApproved()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        var id = submitted!.Id;

        using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
        (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();

        using var cr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/cancel", xsrf, new { note = "not required anymore"});
        var resp = await session.Client.SendAsync(cr, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        Assert.Equal(UpdateRequestStatus.Cancelled, detail!.Status);
    }

    [Fact]
    public async Task Execute_MarksExecuted_EvenOnSqlError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute], ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { serverId, sqlText = "UPDATE public.nonexistent SET foo = 'bar'", reason = "bad sql test" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        var id = submitted!.Id;

        using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
        (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();

        using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
        var resp = await session.Client.SendAsync(er, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
        Assert.Equal(UpdateRequestStatus.Executed, detail!.Status);
        Assert.False(detail.ExecSuccess);
        Assert.NotNull(detail.ExecError);
    }
}