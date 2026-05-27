using SluiceBase.Core.Common;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Tests;

public class UpdateRequestTests
{
    [Fact]
    public void Create_TrimsSqlText()
    {
        var request = UpdateRequest.Create(
            DatabaseId.From(Guid.NewGuid()),
            "ALTER TABLE users ADD COLUMN age INT;\n",
            "Adding age column",
            new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow));

        Assert.Equal("ALTER TABLE users ADD COLUMN age INT;", request.SqlText);
    }

    [Fact]
    public void Create_TrimsReason()
    {
        var request = UpdateRequest.Create(
            DatabaseId.From(Guid.NewGuid()),
            "ALTER TABLE users ADD COLUMN age INT;",
            "  Adding age column\n",
            new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow));

        Assert.Equal("Adding age column", request.Reason);
    }

    [Fact]
    public void Create_PreservesInternalWhitespace()
    {
        var sql = "ALTER TABLE users\n  ADD COLUMN age INT;";
        var request = UpdateRequest.Create(
            DatabaseId.From(Guid.NewGuid()),
            sql,
            "Adding age column",
            new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow));

        Assert.Equal(sql, request.SqlText);
    }
}
