using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
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
}
