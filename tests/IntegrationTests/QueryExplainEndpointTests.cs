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
        // Bob has no query:execute on the database.
        var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query/explain", xsrf,
            new { databaseId = Guid.NewGuid(), sql = "SELECT 1", analyze = false });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.True(resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }
}
