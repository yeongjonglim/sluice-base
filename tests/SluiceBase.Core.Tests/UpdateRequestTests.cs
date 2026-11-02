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

    private static UpdateRequest ApprovedRequest()
    {
        var request = UpdateRequest.Create(
            DatabaseId.From(Guid.NewGuid()), "UPDATE t SET a = 1", "reason",
            new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow));
        request.Approve(new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow), "ok");
        return request;
    }

    [Fact]
    public void BeginExecution_ClaimsApprovedRequestForExecutor()
    {
        var request = ApprovedRequest();
        var executor = UserId.From(Guid.NewGuid());

        request.BeginExecution(new Actioned(executor, DateTimeOffset.UtcNow));

        Assert.Equal(UpdateRequestStatus.Executing, request.Status);
        Assert.Equal(executor, request.ExecutorId);
        Assert.False(request.CanExecute());
    }

    [Fact]
    public void Executing_CannotBeClaimedAgainOrCancelled()
    {
        var request = ApprovedRequest();
        request.BeginExecution(new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow));

        Assert.Throws<InvalidOperationException>(() =>
            request.BeginExecution(new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow)));
        Assert.Throws<InvalidOperationException>(() =>
            request.Cancel(new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow), "stop"));
    }

    [Fact]
    public void RecordExecution_RequiresTheClaimingExecutor()
    {
        var request = ApprovedRequest();
        var executor = UserId.From(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            request.RecordExecution(new Actioned(executor, DateTimeOffset.UtcNow), true, 1, 1, null));

        request.BeginExecution(new Actioned(executor, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            request.RecordExecution(new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow), true, 1, 1, null));

        request.RecordExecution(new Actioned(executor, DateTimeOffset.UtcNow), true, 1, 1, null);
        Assert.Equal(UpdateRequestStatus.Executed, request.Status);
    }
}
