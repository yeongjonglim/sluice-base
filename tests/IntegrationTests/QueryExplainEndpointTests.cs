using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Api.Endpoints;

namespace IntegrationTests;

public sealed class QueryExplainEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    [Fact]
    public async Task Explain_Estimate_Returns200WithPlan()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", xsrf,
            new { databaseId, sql = "SELECT * FROM users", analyze = false });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryPlanResponse>(ct);
        Assert.NotNull(body);
        Assert.NotEmpty(body!.PlanJson);
        Assert.True(body.Summary.TotalCost > 0);
    }

    [Fact]
    public async Task Explain_BadSql_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", xsrf,
            new { databaseId, sql = "SELECT * FROM does_not_exist_xyz", analyze = false });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Explain_WithoutDatabaseRole_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aliceSession, _, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _a = aliceSession;

        // Bob has no database role on the test database
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var bobXsrf = await bobSession.FetchXsrfTokenAsync(ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", bobXsrf,
            new { databaseId, sql = "SELECT 1", analyze = false });
        var resp = await bobSession.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Explain_SensitiveColumn_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", xsrf,
            new { databaseId, sql = "SELECT email FROM public.users LIMIT 1", analyze = false });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        Assert.Equal("sensitive_columns", body.GetProperty("type").GetString());
        var columns = body.GetProperty("columns");
        Assert.True(columns.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Query_SuccessfulSelect_IncludesPlanEstimate()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = "SELECT * FROM users" });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(body);
        Assert.NotNull(body!.Estimate);
        Assert.True(body.Estimate!.TotalCost > 0);
    }
}
