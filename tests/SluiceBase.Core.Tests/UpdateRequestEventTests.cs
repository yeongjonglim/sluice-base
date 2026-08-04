using SluiceBase.Core.Common;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Tests;

public class UpdateRequestEventTests
{
    [Fact]
    public void Preview_SetsFieldsAndGeneratesId()
    {
        var requestId = UpdateRequestId.From(Guid.NewGuid());
        var actor = UserId.From(Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        var evt = UpdateRequestEvent.Preview(
            requestId, new Actioned(actor, at),
            success: true, durationMs: 12, affectedRows: 3, resultSetCount: 2, error: null);

        Assert.NotEqual(Guid.Empty, evt.Id.Value);
        Assert.Equal(requestId, evt.RequestId);
        Assert.Equal(UpdateRequestEventType.Previewed, evt.Type);
        Assert.Equal(actor, evt.ActorId);
        Assert.Equal(at, evt.At);
        Assert.True(evt.Success);
        Assert.Equal(12, evt.DurationMs);
        Assert.Equal(3, evt.AffectedRows);
        Assert.Equal(2, evt.ResultSetCount);
        Assert.Null(evt.Error);
    }

    [Fact]
    public void Preview_CarriesErrorOnFailure()
    {
        var evt = UpdateRequestEvent.Preview(
            UpdateRequestId.From(Guid.NewGuid()),
            new Actioned(UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow),
            success: false, durationMs: 5, affectedRows: 0, resultSetCount: 0, error: "boom");

        Assert.False(evt.Success);
        Assert.Equal("boom", evt.Error);
    }
}
