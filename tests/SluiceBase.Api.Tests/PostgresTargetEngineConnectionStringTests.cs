using SluiceBase.Api.Targets;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Tests;

public class PostgresTargetEngineConnectionStringTests
{
    [Fact]
    public void BuildConnectionString_IncludesAllParameters()
    {
        var engine = new PostgresTargetEngine();

        var cs = engine.BuildConnectionString(
            new ConnectionParameters("appdb", "reader", "s3cret",
                new PostgresConnectionOptions("db.example.com", 5433)));

        Assert.Contains("Host=db.example.com", cs);
        Assert.Contains("Port=5433", cs);
        Assert.Contains("Database=appdb", cs);
        Assert.Contains("Username=reader", cs);
        Assert.Contains("Password=s3cret", cs);
    }
}
