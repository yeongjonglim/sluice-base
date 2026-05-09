using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public sealed class TargetEngineTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Succeeds()
    {
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        var engine = new PostgresTargetEngine();
        var result = await engine.TestConnectionAsync(
            connectionString,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("postgres", engine.Kind);
    }

    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Fails_OnBadConnString()
    {
        const string brokenConnectionString =
            "Host=does-not-exist.invalid;Port=65000;Database=appdb;Username=u;Password=p;Timeout=2";

        var engine = new PostgresTargetEngine();
        var result = await engine.TestConnectionAsync(
            brokenConnectionString,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ReturnsPublicSchemaForBlue()
    {
        await factory.InitialisedApp.ResourceNotifications
            .WaitForResourceHealthyAsync("blue-appdb", TestContext.Current.CancellationToken);

        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        var engine = new PostgresTargetEngine();
        var tree = await engine.GetSchemaAsync(
            connectionString,
            TestContext.Current.CancellationToken);

        Assert.NotNull(tree);
        Assert.DoesNotContain(tree.Schemas, s => s.Name == "information_schema");
        Assert.DoesNotContain(tree.Schemas, s => s.Name == "pg_catalog");
        var publicSchema = Assert.Single(tree.Schemas, s => s.Name == "public");
        Assert.Contains(publicSchema.Tables, t => t.Name == "users");
        var usersTable = publicSchema.Tables.Single(t => t.Name == "users");
        Assert.NotEmpty(usersTable.Columns);
        Assert.All(usersTable.Columns, c => Assert.NotEmpty(c.DataType));
    }
}