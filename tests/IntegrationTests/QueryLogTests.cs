using SluiceBase.Core.Queries;

namespace IntegrationTests;

public class QueryLogTests
{
    [Fact]
    public void Create_TrimsLeadingAndTrailingWhitespace_FromQueryText()
    {
        var log = QueryLog.Create(null, null, "  SELECT 1  ", QueryLogStatus.Success,
            DateTimeOffset.UtcNow, 10, 1, null);

        Assert.Equal("SELECT 1", log.QueryText);
    }

    [Fact]
    public void Create_TrimsNewlinesAroundQuery()
    {
        var log = QueryLog.Create(null, null, "\n\nSELECT 1\n\n", QueryLogStatus.Success,
            DateTimeOffset.UtcNow, 10, 1, null);

        Assert.Equal("SELECT 1", log.QueryText);
    }

    [Fact]
    public void Create_PreservesInternalWhitespace()
    {
        var log = QueryLog.Create(null, null, "  SELECT id, name\nFROM users  ", QueryLogStatus.Success,
            DateTimeOffset.UtcNow, 10, 1, null);

        Assert.Equal("SELECT id, name\nFROM users", log.QueryText);
    }
}
