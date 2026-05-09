using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public sealed class TargetEngineTests(SluiceBaseStackFactory factory)
{
    private readonly PostgresTargetEngine _targetEngine = new();

    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Succeeds()
    {
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        var result = await _targetEngine.TestConnectionAsync(
            connectionString,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("postgres", _targetEngine.Kind);
    }

    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Fails_OnBadConnString()
    {
        const string brokenConnectionString =
            "Host=does-not-exist.invalid;Port=65000;Database=appdb;Username=u;Password=p;Timeout=2";

        var result = await _targetEngine.TestConnectionAsync(
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

        var tree = await _targetEngine.GetSchemaAsync(
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

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteQuery_ReturnsData()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var result = await _targetEngine.ExecuteQueryAsync(
            connectionString,
            "SELECT id, email FROM public.users ORDER BY id LIMIT 5",
            ct);

        Assert.NotNull(result);
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("id", result.Columns[0]);
        Assert.Equal("email", result.Columns[1]);
        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, row => Assert.Equal(2, row.Length));
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteQuery_ReturnsEmptyRows_ForNoResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var result = await _targetEngine.ExecuteQueryAsync(
            connectionString,
            "SELECT id FROM public.users WHERE 1 = 0",
            ct);

        Assert.NotNull(result);
        Assert.Single(result.Columns);
        Assert.Empty(result.Rows);
    }
}