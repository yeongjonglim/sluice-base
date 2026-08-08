using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public class SensitiveColumnResolutionTests(SluiceBaseStackFactory factory)
{
    private async Task<(string conn, string schema)> SeedAsync(CancellationToken ct)
    {
        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);
        return (conn, schema);
    }

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
}
