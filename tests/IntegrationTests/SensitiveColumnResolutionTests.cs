using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace IntegrationTests;

public class SensitiveColumnResolutionTests(SluiceBaseStackFactory factory)
{
    private async Task<(string conn, string schema)> SeedAsync(CancellationToken ct)
    {
        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);
        return (conn, schema);
    }

    // Sets up Alice with a write-capable database and update:submit — enough to submit and
    // preview her own update requests (mirrors UpdateEndpointTests.AliceWithBlueServerAsync).
    private async Task<(AuthenticatedSession session, string xsrf, DatabaseId databaseId)>
        AliceWithWritableBlueServerAsync(CancellationToken ct)
    {
        var loginHelper = new KeycloakLoginHelper(factory.InitialisedApp);
        var session = await loginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        using var grantServer = QueryTestSetup.MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"scp-{Guid.NewGuid():N}"[..24];
        using var sReq = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = QueryTestSetup.MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("Read-only role", "reader_blue", "reader_blue"));
        var rcResp = await session.Client.SendAsync(rcReq, ct);
        rcResp.EnsureSuccessStatusCode();
        var readCred = (await rcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var wcReq = QueryTestSetup.MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("Write role", "writer_blue", "writer_blue"));
        var wcResp = await session.Client.SendAsync(wcReq, ct);
        wcResp.EnsureSuccessStatusCode();
        var writeCred = (await wcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = QueryTestSetup.MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id, writeCred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var database = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        await DatabaseRoleTestHelper.AssignByDatabaseAsync(
            session, alice.Id, Permissions.UpdateSubmit, database.Id.ToString(), xsrf, ct);

        return (session, xsrf, database.Id);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);

    [Fact]
    public async Task Resolve_JoinWithWhere_AttributesColumnsToCorrectRelations()
    {
        var ct = TestContext.Current.CancellationToken;
        var (conn, schema) = await SeedAsync(ct);
        var engine = new PostgresTargetEngine();

        var cols = await engine.ResolveReferencedColumnsAsync(conn,
            $"""SELECT u.name FROM "{schema}".users u JOIN "{schema}".orders o ON o.customer_id = u.id WHERE u.ssn IS NOT NULL""",
            ct);

        Assert.Contains(cols, c => c.Schema == schema && c.Table == "users" && c.Column == "ssn");
        Assert.Contains(cols, c => c.Schema == schema && c.Table == "orders" && c.Column == "customer_id");
        Assert.DoesNotContain(cols, c => c.Table == "orders" && c.Column == "ssn");
    }

    [Fact]
    public async Task Resolve_View_SeesThroughToBaseColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var (conn, schema) = await SeedAsync(ct);
        var engine = new PostgresTargetEngine();

        var cols = await engine.ResolveReferencedColumnsAsync(conn,
            $"""SELECT national_id FROM "{schema}".v_users""", ct);

        Assert.Contains(cols, c => c.Schema == schema && c.Table == "users" && c.Column == "ssn");
    }

    [Fact]
    public async Task Query_SameColumnNameOnOtherTable_NotBlockedNorMisattributed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);

        // users.email is sensitive, but contacts also has a column named "email" that is not.
        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), schema, "users", "email", xsrf, ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, $"""SELECT email FROM "{schema}".contacts"""));
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Query_SelectStar_DoesNotBlockOtherTablesSensitiveColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);

        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), schema, "users", "email", xsrf, ct);
        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), schema, "orders", "amount", xsrf, ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, $"""SELECT * FROM "{schema}".users"""));
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("sensitive_columns", body.GetProperty("type").GetString());
        var columns = body.GetProperty("columns").EnumerateArray().ToList();
        var column = Assert.Single(columns);
        Assert.Equal("users", column.GetProperty("table").GetString());
        Assert.Equal("email", column.GetProperty("column").GetString());
    }

    [Fact]
    public async Task Query_DenylistedXmlFunction_BlockedWithReasonNoColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);

        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), schema, "users", "email", xsrf, ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, "SELECT query_to_xml('SELECT 1', true, false, '')"));
        var resp = await session.Client.SendAsync(req, ct);

        // Cross-task: PolicyBlockReason only surfaces as a 403 with a reason once Task 7 wires
        // it through IQueryService/QueryEndpoints. Green in CI only after that task lands.
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("sensitive_columns", body.GetProperty("type").GetString());
        Assert.Empty(body.GetProperty("columns").EnumerateArray());
        Assert.Contains("query_to_xml", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task UpdatePreview_DenylistedXmlFunction_BlockedWithReasonNoColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceWithWritableBlueServerAsync(ct);
        using var _ = session;

        // Any sensitive column on the database is enough to activate the denylist check —
        // the blocked query need not touch it (see SensitiveColumnGuard.EvaluateAsync).
        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);

        using var submitReq = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/update", xsrf,
            new UpdateEndpoints.SubmitUpdateRequest(
                databaseId, "SELECT query_to_xml('SELECT 1', true, false, '')", "preview test"));
        var submitResp = await session.Client.SendAsync(submitReq, ct);
        submitResp.EnsureSuccessStatusCode();
        using var submitDoc = JsonDocument.Parse(await submitResp.Content.ReadAsStringAsync(ct));
        var id = submitDoc.RootElement.GetProperty("id").GetGuid();

        using var previewReq = QueryTestSetup.MutationRequest(HttpMethod.Post, $"/api/update/{id}/preview", xsrf);
        var resp = await session.Client.SendAsync(previewReq, ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("sensitive_columns", body.GetProperty("type").GetString());
        Assert.Empty(body.GetProperty("columns").EnumerateArray());
        Assert.Contains("query_to_xml", body.GetProperty("reason").GetString());
    }

    // We have NOT empirically confirmed (Docker is unavailable in this environment) whether
    // Postgres renders whole-row serialization of `u` in the scan node's Output as enumerated
    // columns ("users.email", "users.ssn") or as a whole-row var, which
    // PostgresPlanColumnExtractor surfaces as ColumnRef(schema, "users", "*") — see its doc
    // comment and Extract_QualifiedStar_EmitsWholeRelationMarker in
    // PostgresPlanColumnExtractorTests. Every serializer test below must accept EITHER form
    // rather than pin an exact column name, so the resolver is never asserted to under-block.
    private static bool CoversUsersSensitive(IReadOnlyList<ColumnRef> cols, string schema) =>
        cols.Any(c => c.Schema == schema && c.Table == "users"
                      && (c.Column == "*" || c.Column == "email" || c.Column == "ssn"));

    // Brute-force battery: every documented way to serialize a whole row (or a sensitive
    // column via a non-trivial expression) must still surface users.email/users.ssn to the
    // resolver, whether as enumerated columns or the `*` whole-row marker. hstore(u) is
    // intentionally omitted — it requires `CREATE EXTENSION hstore`, which the fixture does
    // not install.
    [Theory]
    [InlineData("""SELECT to_jsonb(u) FROM "{0}".users u""")]
    [InlineData("""SELECT to_json(u) FROM "{0}".users u""")]
    [InlineData("""SELECT row_to_json(u) FROM "{0}".users u""")]
    [InlineData("""SELECT json_agg(u) FROM "{0}".users u""")]
    [InlineData("""SELECT jsonb_agg(u) FROM "{0}".users u""")]
    [InlineData("""SELECT jsonb_build_object('e', u.email) FROM "{0}".users u""")]
    [InlineData("""SELECT array_to_json(array_agg(u)) FROM "{0}".users u""")]
    [InlineData("""SELECT array_agg(u) FROM "{0}".users u""")]
    [InlineData("""SELECT xmlelement(name r, u.*) FROM "{0}".users u""")]
    [InlineData("""SELECT xmlforest(u.email AS email, u.ssn AS ssn) FROM "{0}".users u""")]
    [InlineData("""SELECT xmlagg(xmlelement(name r, u.*)) FROM "{0}".users u""")]
    [InlineData("""SELECT u FROM "{0}".users u""")]
    [InlineData("""SELECT u::text FROM "{0}".users u""")]
    [InlineData("""SELECT u.* FROM "{0}".users u""")]
    [InlineData("""SELECT * FROM "{0}".users""")]
    [InlineData("""SELECT encode(convert_to(u.email,'UTF8'),'base64') FROM "{0}".users u""")]
    public async Task Resolve_ExpressionArgSerializer_NeverUnderBlocksUsersSensitiveColumns(string sqlTemplate)
    {
        var ct = TestContext.Current.CancellationToken;
        var (conn, schema) = await SeedAsync(ct);
        var engine = new PostgresTargetEngine();

        var sql = sqlTemplate.Replace("{0}", schema, StringComparison.Ordinal);
        var cols = await engine.ResolveReferencedColumnsAsync(conn, sql, ct);

        Assert.True(CoversUsersSensitive(cols, schema),
            $"Resolver failed to flag users.email/ssn (enumerated or whole-row '*') for: {sql}");
        // No other seeded table's columns should show up for these single-table queries.
        Assert.DoesNotContain(cols, c => c.Table is "contacts" or "orders");
    }

    [Fact]
    public async Task Query_QueryToXml_NotBlocked_WhenNoSensitiveColumnsMarked()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);

        // Zero sensitive columns marked anywhere on this database: SensitiveColumnGuard.EvaluateAsync
        // short-circuits (sensitiveColumns.Count == 0) BEFORE the denylist check ever runs, so a
        // denylisted XML-export function is not blocked while the policy is dormant.
        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId,
                $"""SELECT query_to_xml('SELECT ssn FROM "{schema}".users', true, false, '')"""));
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Query_DenylistedNameInsideStringLiteral_DoesNotTripBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);

        // Mark a sensitive column so the guard's denylist check actually runs — it is dormant,
        // and therefore vacuously non-blocking, when the database has zero sensitive columns
        // (see the test above). The literal query text below never selects users.email, so a
        // block here could only come from a false-positive denylist match on "table_to_xml"
        // appearing inside a string literal rather than as an identifier;
        // SerializationFunctionDenylist.FindFirst tokenizes first, so it must not match here.
        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), schema, "users", "email", xsrf, ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId,
                $"""SELECT 'table_to_xml' FROM "{schema}".users"""));
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // Odd-shaped SQL (nested subqueries, recursion, set operations, DISTINCT ON, window
    // functions) must never crash the resolver, and when it genuinely reads a sensitive
    // column, that read must still be flagged — enumerated or via the `*` whole-row marker.
    [Theory]
    [InlineData("""SELECT x.ssn FROM (SELECT ssn FROM "{0}".users) x""")]
    [InlineData("""
        WITH RECURSIVE r AS (
            SELECT id, ssn FROM "{0}".users WHERE id = 1
            UNION ALL
            SELECT u.id, u.ssn FROM "{0}".users u JOIN r ON u.id = r.id + 1
        )
        SELECT ssn FROM r
        """)]
    [InlineData("""SELECT ssn FROM "{0}".users UNION SELECT phone FROM "{0}".contacts""")]
    [InlineData("""SELECT DISTINCT ON (u.ssn) u.ssn, u.name FROM "{0}".users u ORDER BY u.ssn""")]
    [InlineData("""SELECT row_number() OVER (ORDER BY u.ssn) FROM "{0}".users u""")]
    public async Task Resolve_RobustnessCase_DoesNotThrow_AndFlagsSsnWhereGenuinelyRead(string sqlTemplate)
    {
        var ct = TestContext.Current.CancellationToken;
        var (conn, schema) = await SeedAsync(ct);
        var engine = new PostgresTargetEngine();
        var sql = sqlTemplate.Replace("{0}", schema, StringComparison.Ordinal);

        IReadOnlyList<ColumnRef>? cols = null;
        var ex = await Record.ExceptionAsync(async () =>
        {
            cols = await engine.ResolveReferencedColumnsAsync(conn, sql, ct);
        });

        Assert.Null(ex);
        Assert.NotNull(cols);
        Assert.True(CoversUsersSensitive(cols!, schema),
            $"Resolver failed to flag users.ssn (enumerated or whole-row '*') for: {sql}");
    }
}
