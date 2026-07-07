using SluiceBase.Core.Servers;

namespace SluiceBase.Core.Tests;

public class ServerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void PostgresServer_Create_SetsCommonFieldsAndKind()
    {
        var server = PostgresServer.Create("pg", "localhost", 5432, Now);

        Assert.Equal("postgres", server.Kind);
        Assert.Equal("pg", server.Name);
        Assert.Equal("localhost", server.Host);
        Assert.Equal(5432, server.Port);
        Assert.False(server.IsDisabled);
    }

    [Fact]
    public void MongoServer_Create_SetsMongoOptions()
    {
        var server = MongoServer.Create("mongo", "cluster0.mongodb.net", 27017, Now,
            ConnectionMode.Srv, authSource: "admin", replicaSet: "rs0", useTls: true);

        Assert.Equal("mongodb", server.Kind);
        Assert.Equal(ConnectionMode.Srv, server.ConnectionMode);
        Assert.Equal("admin", server.AuthSource);
        Assert.Equal("rs0", server.ReplicaSet);
        Assert.True(server.UseTls);
    }

    [Fact]
    public void MongoServer_UpdateMongo_OverwritesOptions()
    {
        var server = MongoServer.Create("mongo", "h", 27017, Now,
            ConnectionMode.Srv, authSource: "admin", replicaSet: null, useTls: false);

        server.UpdateMongo(ConnectionMode.Standard, authSource: null, replicaSet: "rs1", useTls: true);

        Assert.Equal(ConnectionMode.Standard, server.ConnectionMode);
        Assert.Null(server.AuthSource);
        Assert.Equal("rs1", server.ReplicaSet);
        Assert.True(server.UseTls);
    }

    [Fact]
    public void UpdateCore_UpdatesCommonFields()
    {
        var server = PostgresServer.Create("pg", "h1", 5432, Now);

        server.UpdateCore("pg2", "h2", 5433, isDisabled: true, Now.AddMinutes(1));

        Assert.Equal("pg2", server.Name);
        Assert.Equal("h2", server.Host);
        Assert.Equal(5433, server.Port);
        Assert.True(server.IsDisabled);
    }
}
