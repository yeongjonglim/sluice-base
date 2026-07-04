using SluiceBase.Core.Servers;

namespace SluiceBase.Core.Tests;

public class ServerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Create_DefaultsToStandardModeWithNoMongoOptions()
    {
        var server = Server.Create("pg", "postgres", "localhost", 5432, Now);

        Assert.Equal(ConnectionMode.Standard, server.ConnectionMode);
        Assert.Null(server.AuthSource);
        Assert.Null(server.ReplicaSet);
        Assert.False(server.UseTls);
    }

    [Fact]
    public void Create_SetsMongoOptionsWhenProvided()
    {
        var server = Server.Create("mongo", "mongodb", "cluster0.mongodb.net", 27017, Now,
            ConnectionMode.Srv, authSource: "admin", replicaSet: "rs0", useTls: true);

        Assert.Equal(ConnectionMode.Srv, server.ConnectionMode);
        Assert.Equal("admin", server.AuthSource);
        Assert.Equal("rs0", server.ReplicaSet);
        Assert.True(server.UseTls);
    }

    [Fact]
    public void Update_OverwritesMongoOptions()
    {
        var server = Server.Create("mongo", "mongodb", "h", 27017, Now,
            ConnectionMode.Srv, authSource: "admin");

        server.Update("mongo", "h2", 27017, "mongodb", isDisabled: false, Now,
            ConnectionMode.Standard, authSource: null, replicaSet: "rs1", useTls: true);

        Assert.Equal(ConnectionMode.Standard, server.ConnectionMode);
        Assert.Null(server.AuthSource);
        Assert.Equal("rs1", server.ReplicaSet);
        Assert.True(server.UseTls);
    }
}
