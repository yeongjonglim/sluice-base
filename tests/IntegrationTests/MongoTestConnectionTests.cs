using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public sealed class MongoTestConnectionTests(SluiceBaseStackFactory factory)
{
    private readonly MongoTargetEngine _engine = new();

    [Fact]
    public async Task Mongo_TestConnection_Succeeds()
    {
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("mongo-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        var result = await _engine.TestConnectionAsync(
            connectionString!, TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("mongodb", _engine.Kind);
    }

    [Fact]
    public async Task Mongo_TestConnection_Fails_OnBadConnString()
    {
        const string broken =
            "mongodb://u:p@does-not-exist.invalid:65000/appdb?serverSelectionTimeoutMS=2000";

        var result = await _engine.TestConnectionAsync(
            broken, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
