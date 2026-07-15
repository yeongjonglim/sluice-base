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
            new ConnectionParameters("db.example.com", 5433, "appdb", "reader", "s3cret"));

        Assert.Contains("Host=db.example.com", cs);
        Assert.Contains("Port=5433", cs);
        Assert.Contains("Database=appdb", cs);
        Assert.Contains("Username=reader", cs);
        Assert.Contains("Password=s3cret", cs);
    }
}
