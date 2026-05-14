using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase_Core;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;

namespace IntegrationTests;

public class UpdateEndpointTests
{
    private readonly SluiceBaseStackFactory _factory;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public UpdateEndpointTests(SluiceBaseStackFactory factory)
    {
        _factory = factory;
        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        _jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        _jsonSerializerOptions.Converters.Add(new VogenTypesFactory());
    }

    private KeycloakLoginHelper LoginHelper => new(_factory.InitialisedApp);

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

    // Sets up Alice with all update permissions and returns the blue database's ID
    private async Task<(AuthenticatedSession session, string xsrf, DatabaseId databaseId)> AliceWithBlueServerAsync(
        string[] permissions,
        CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        // Grant server:manage so we can create the server
        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        foreach (var perm in permissions)
        {
            using var grant = MutationRequest(HttpMethod.Post,
                $"/api/admin/user/{alice.Id}/permission",
                xsrf,
                new { permission = perm });
            (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();
        }

        var blueConnStr = await _factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"upd-{Guid.NewGuid():N}"[..24];
        using var sReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("Read-only role", "reader_blue", "reader_blue"));
        var rcResp = await session.Client.SendAsync(rcReq, ct);
        rcResp.EnsureSuccessStatusCode();
        var readCred = (await rcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var wcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("Write role", "writer_blue", "writer_blue"));
        var wcResp = await session.Client.SendAsync(wcReq, ct);
        wcResp.EnsureSuccessStatusCode();
        var writeCred = (await wcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id, writeCred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var database = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (session, xsrf, database.Id);
    }

    // ── auth guards ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostUpdate_Returns401_ForAnonymous()
    {
        using var client = _factory.InitialisedApp.CreateHttpClient("api", "https");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/update");
        req.Content = JsonContent.Create(new { databaseId = Guid.NewGuid(), sqlText = "UPDATE x", reason = "r" });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PostUpdate_Returns403_ForUserWithoutPermission()
    {
        var ct = TestContext.Current.CancellationToken;
        using var adminSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        await PermissionTestHelper.RevokeAllPermissionsAsync(adminSession, "bob@example.com", await adminSession.FetchXsrfTokenAsync(ct), ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        using var req = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new { databaseId = Guid.NewGuid(), sqlText = "UPDATE x", reason = "r" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetUpdates_Returns403_ForUserWithoutAnyUpdatePermission()
    {
        var ct = TestContext.Current.CancellationToken;

        using var adminSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);

        await PermissionTestHelper.RevokeAllPermissionsAsync(adminSession, "bob@example.com", await adminSession.FetchXsrfTokenAsync(ct), ct);

        var resp = await session.Client.GetAsync("/api/update", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── submit ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostUpdate_Returns201_WithPendingStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit],
            ct);
        using var _ = session;

        using var req = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new
            {
                databaseId,
                sqlText = "UPDATE public.users SET email = email WHERE 1=0",
                reason = "test submission",
            });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var detail = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
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
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit],
            ct);
        using var _ = session;

        // Submit one first
        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new
            {
                databaseId,
                sqlText = "UPDATE public.users SET email = email WHERE 1=0",
                reason = "list test",
            });
        (await session.Client.SendAsync(submitReq, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetAsync("/api/update", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var list = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(_jsonSerializerOptions, ct);
        Assert.NotNull(list);
        Assert.NotEmpty(list.Requests);
    }

    // ── full happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task FullFlow_Pending_Approved_Executed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute],
            ct);
        using var _ = session;

        // Submit
        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new
            {
                databaseId,
                sqlText = "UPDATE public.users SET email = email WHERE 1=0",
                reason = "happy path test",
            });
        var submitResp = await session.Client.SendAsync(submitReq, ct);
        submitResp.EnsureSuccessStatusCode();
        var submitted = await submitResp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        Assert.Equal(UpdateRequestStatus.Pending, submitted!.Status);
        var requestId = submitted.Id.Value;

        // Approve
        using var approveReq = MutationRequest(HttpMethod.Post,
            $"/api/update/{requestId}/approve",
            xsrf,
            new { note = "looks good" });
        var approveResp = await session.Client.SendAsync(approveReq, ct);
        approveResp.EnsureSuccessStatusCode();
        var approved = await approveResp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        Assert.Equal(UpdateRequestStatus.Approved, approved!.Status);
        Assert.Equal("looks good", approved.ReviewNote);

        // Execute
        using var executeReq = MutationRequest(HttpMethod.Post,
            $"/api/update/{requestId}/execute",
            xsrf);
        var executeResp = await session.Client.SendAsync(executeReq, ct);
        executeResp.EnsureSuccessStatusCode();
        var executed = await executeResp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
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
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute],
            ct);
        using var _ = session;

        // Submit → Approve → Execute
        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new { databaseId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        var id = submitted!.Id.Value;

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
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateExecute],
            ct);
        using var _ = session;

        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new { databaseId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var responseMessage = (await session.Client.SendAsync(submitReq, ct));
        var submitted = await responseMessage.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        var id = submitted!.Id.Value;

        // using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
        // var resp = await session.Client.SendAsync(er, ct);
        // Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns409_WhenAlreadyRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove],
            ct);
        using var _ = session;

        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new { databaseId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        var id = submitted!.Id.Value;

        using var rr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/reject", xsrf, new { note = "no" });
        (await session.Client.SendAsync(rr, ct)).EnsureSuccessStatusCode();

        using var cr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/cancel", xsrf, new { note = "not required anymore" });
        var resp = await session.Client.SendAsync(cr, ct);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns200_WhenApproved()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove],
            ct);
        using var _ = session;

        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new { databaseId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        var id = submitted!.Id.Value;

        using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
        (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();

        using var cr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/cancel", xsrf, new { note = "not required anymore" });
        var resp = await session.Client.SendAsync(cr, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        Assert.Equal(UpdateRequestStatus.Cancelled, detail!.Status);
    }

    [Fact]
    public async Task Execute_MarksExecuted_EvenOnSqlError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute],
            ct);
        using var _ = session;

        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new { databaseId, sqlText = "UPDATE public.nonexistent SET foo = 'bar'", reason = "bad sql test" });
        var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        var id = submitted!.Id.Value;

        using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
        (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();

        using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
        var resp = await session.Client.SendAsync(er, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await resp.Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);
        Assert.Equal(UpdateRequestStatus.Executed, detail!.Status);
        Assert.False(detail.ExecSuccess);
        Assert.NotNull(detail.ExecError);
    }

    [Fact]
    public async Task Submit_DisabledDatabase_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit],
            ct);
        using var _ = session;

        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        var srv = list!.Servers.First(s => s.Databases.Any(d => d.Id == databaseId));
        var db = srv.Databases.First(d => d.Id == databaseId);

        using var disableReq = MutationRequest(HttpMethod.Put,
            $"/api/server/{srv.Id}/database/{db.Id}",
            xsrf,
            new DatabaseEndpoints.UpdateDatabaseRequest(db.DisplayName, db.DatabaseName, db.ReadCredentialId, db.WriteCredentialId, true));
        (await session.Client.SendAsync(disableReq, ct)).EnsureSuccessStatusCode();

        using var submitReq = MutationRequest(HttpMethod.Post,
            "/api/update",
            xsrf,
            new UpdateEndpoints.SubmitUpdateRequest(databaseId, "UPDATE foo SET bar = 1", "test"));
        Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.SendAsync(submitReq, ct)).StatusCode);
    }

    // ── filter tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUpdates_FiltersByStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit, Permissions.UpdateApprove],
            ct);
        using var _ = session;

        var reason1 = $"status-filter-{Guid.NewGuid():N}";
        var reason2 = $"status-filter-{Guid.NewGuid():N}";

        using var r1 = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { databaseId, sqlText = "UPDATE t SET a=1 WHERE 1=0", reason = reason1 });
        var detail1 = await (await session.Client.SendAsync(r1, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);

        using var r2 = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { databaseId, sqlText = "UPDATE t SET b=2 WHERE 1=0", reason = reason2 });
        var detail2 = await (await session.Client.SendAsync(r2, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);

        // Approve the second request only
        using var approveReq = MutationRequest(HttpMethod.Post,
            $"/api/update/{detail2!.Id.Value}/approve", xsrf, new { note = "ok" });
        (await session.Client.SendAsync(approveReq, ct)).EnsureSuccessStatusCode();

        // Filter by Pending — includes first, excludes second
        var pendingList = await session.Client
            .GetFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(
                "/api/update?status=Pending", _jsonSerializerOptions, ct);
        Assert.NotNull(pendingList);
        Assert.Contains(pendingList.Requests, r => r.Id == detail1!.Id);
        Assert.DoesNotContain(pendingList.Requests, r => r.Id == detail2.Id);

        // Filter by Approved — includes second, excludes first
        var approvedList = await session.Client
            .GetFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(
                "/api/update?status=Approved", _jsonSerializerOptions, ct);
        Assert.NotNull(approvedList);
        Assert.Contains(approvedList.Requests, r => r.Id == detail2.Id);
        Assert.DoesNotContain(approvedList.Requests, r => r.Id == detail1!.Id);
    }

    [Fact]
    public async Task GetUpdates_FiltersByDatabaseId()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit],
            ct);
        using var _ = session;

        var reason = $"db-filter-{Guid.NewGuid():N}";
        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { databaseId, sqlText = "UPDATE t SET a=1 WHERE 1=0", reason });
        var detail = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);

        // Filter by known database — finds the request
        var found = await session.Client
            .GetFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(
                $"/api/update?databaseId={databaseId}", _jsonSerializerOptions, ct);
        Assert.NotNull(found);
        Assert.Contains(found.Requests, r => r.Id == detail!.Id);

        // Filter by random database ID — does not include this request
        var filtered = await session.Client
            .GetFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(
                $"/api/update?databaseId={Guid.NewGuid()}", _jsonSerializerOptions, ct);
        Assert.NotNull(filtered);
        Assert.DoesNotContain(filtered.Requests, r => r.Id == detail!.Id);
    }

    [Fact]
    public async Task GetUpdates_FiltersByDateRange()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(
            [Permissions.UpdateSubmit],
            ct);
        using var _ = session;

        var reason = $"date-filter-{Guid.NewGuid():N}";
        using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new { databaseId, sqlText = "UPDATE t SET a=1 WHERE 1=0", reason });
        var detail = await (await session.Client.SendAsync(submitReq, ct)).Content
            .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(_jsonSerializerOptions, ct);

        // Far-future `to` — excludes the request
        var noResults = await session.Client
            .GetFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(
                "/api/update?to=2000-01-01", _jsonSerializerOptions, ct);
        Assert.NotNull(noResults);
        Assert.DoesNotContain(noResults.Requests, r => r.Id == detail!.Id);

        // Far-past `from` — includes the request
        var withResults = await session.Client
            .GetFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(
                "/api/update?from=2020-01-01", _jsonSerializerOptions, ct);
        Assert.NotNull(withResults);
        Assert.Contains(withResults.Requests, r => r.Id == detail!.Id);
    }

    private sealed record ListUserBody(UserRow[] Users);

    private sealed record UserRow(string Id, string Email);
}