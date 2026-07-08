using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Tests;

public class MongoTargetEngineTests
{
    private static readonly MongoTargetEngine Engine = new();

    [Fact]
    public void Kind_IsMongodb()
    {
        Assert.Equal("mongodb", Engine.Kind);
    }

    [Fact]
    public void BuildConnectionString_Standard_UsesHostAndPort()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "shop", "reader", "s3cret",
            new MongoConnectionOptions("db.example.com", ConnectionMode.Standard, 27018, null, null, false)));

        var url = MongoUrl.Create(cs);
        Assert.Equal(ConnectionStringScheme.MongoDB, url.Scheme);
        Assert.Equal("db.example.com", url.Server.Host);
        Assert.Equal(27018, url.Server.Port);
        Assert.Equal("shop", url.DatabaseName);
        Assert.Equal("reader", url.Username);
        Assert.Equal("s3cret", url.Password);
    }

    [Fact]
    public void BuildConnectionString_Srv_UsesSrvSchemeAndNoPort()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "shop", "reader", "s3cret",
            new MongoConnectionOptions("cluster0.ab12.mongodb.net", ConnectionMode.Srv, null, null, null, false)));

        Assert.StartsWith("mongodb+srv://", cs);
        var url = MongoUrl.Create(cs);
        Assert.Equal(ConnectionStringScheme.MongoDBPlusSrv, url.Scheme);
        Assert.Equal("cluster0.ab12.mongodb.net", url.Server.Host);
        Assert.Equal("shop", url.DatabaseName);
    }

    [Fact]
    public void BuildConnectionString_IncludesOptionsWhenSet()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "shop", "u", "p",
            new MongoConnectionOptions("h", ConnectionMode.Standard, 27017, AuthSource: "admin", ReplicaSet: "rs0", UseTls: true)));

        var url = MongoUrl.Create(cs);
        Assert.Equal("admin", url.AuthenticationSource);
        Assert.Equal("rs0", url.ReplicaSetName);
        Assert.True(url.UseTls);
    }

    [Fact]
    public void BuildConnectionString_EscapesCredentials()
    {
        var cs = Engine.BuildConnectionString(new ConnectionParameters(
            "shop", "user name", "p@ss:word/!",
            new MongoConnectionOptions("h", ConnectionMode.Standard, 27017, null, null, false)));

        // MongoUrl.Create round-trips the percent-encoded credentials back to originals.
        var url = MongoUrl.Create(cs);
        Assert.Equal("user name", url.Username);
        Assert.Equal("p@ss:word/!", url.Password);
    }

    [Fact]
    public async Task GetSchemaAsync_Throws_NotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Engine.GetSchemaAsync("mongodb://h/db", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteQueryAsync_Throws_NotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Engine.ExecuteQueryAsync("mongodb://h/db", "{}", CancellationToken.None));
    }
}
