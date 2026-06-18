using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Tests;

public class QueryLogTests
{
    [Fact]
    public void Create_TrimsTrailingWhitespace()
    {
        var log = QueryLog.Create(
            UserId.From(Guid.NewGuid()),
            DatabaseId.From(Guid.NewGuid()),
            "SELECT 1;\n",
            QueryLogStatus.Success,
            DateTimeOffset.UtcNow,
            durationMs: 10,
            rowCount: 1,
            error: null);

        Assert.Equal("SELECT 1;", log.QueryText);
    }

    [Fact]
    public void Create_TrimsLeadingWhitespace()
    {
        var log = QueryLog.Create(
            UserId.From(Guid.NewGuid()),
            DatabaseId.From(Guid.NewGuid()),
            "\n  SELECT 1;",
            QueryLogStatus.Success,
            DateTimeOffset.UtcNow,
            durationMs: 10,
            rowCount: 1,
            error: null);

        Assert.Equal("SELECT 1;", log.QueryText);
    }

    [Fact]
    public void Create_PreservesInternalWhitespace()
    {
        var sql = "SELECT *\nFROM users\nWHERE id = 1;";
        var log = QueryLog.Create(
            UserId.From(Guid.NewGuid()),
            DatabaseId.From(Guid.NewGuid()),
            sql,
            QueryLogStatus.Success,
            DateTimeOffset.UtcNow,
            durationMs: 10,
            rowCount: 1,
            error: null);

        Assert.Equal(sql, log.QueryText);
    }

    [Fact]
    public void Create_DefaultsSourceToUi()
    {
        var log = QueryLog.Create(null, null, "select 1", QueryLogStatus.Success,
            DateTimeOffset.UnixEpoch, 1, 0, null);
        Assert.Equal(QuerySource.Ui, log.Source);
    }

    [Fact]
    public void Create_RecordsMcpSource()
    {
        var log = QueryLog.Create(null, null, "select 1", QueryLogStatus.Success,
            DateTimeOffset.UnixEpoch, 1, 0, null, source: QuerySource.Mcp);
        Assert.Equal(QuerySource.Mcp, log.Source);
    }
}
