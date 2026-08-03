using SluiceBase.Core.Common;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Updates;

// Append-only audit record for events that can occur more than once on a request
// (currently only Previewed). Single-shot lifecycle events remain on UpdateRequest's
// own columns; the timeline merges the two.
public sealed class UpdateRequestEvent
{
#pragma warning disable CS8618
    private UpdateRequestEvent() { }
#pragma warning restore CS8618

    public UpdateRequestEventId Id { get; private set; }
    public UpdateRequestId RequestId { get; private set; }
    public UpdateRequestEventType Type { get; private set; }
    public UserId? ActorId { get; private set; }
    public DateTimeOffset At { get; private set; }

    // General text slot so the deferred full migration of lifecycle events fits
    // this same table: reason (Submitted), review note (Approved/Rejected), cancel
    // note (Cancelled), edit rationale (Edited). Null for Previewed.
    public string? Note { get; private set; }

    public bool? Success { get; private set; }
    public int? DurationMs { get; private set; }
    public int? AffectedRows { get; private set; }
    public int? ResultSetCount { get; private set; }
    public string? Error { get; private set; }

    // Linked by EF relationship
    public UpdateRequest? Request { get; private set; }
    public User? Actor { get; private set; }

    // Row data is never stored — only this metadata is persisted.
    public static UpdateRequestEvent Preview(
        UpdateRequestId requestId,
        Actioned by,
        bool success,
        int durationMs,
        int affectedRows,
        int resultSetCount,
        string? error) => new()
    {
        Id = UpdateRequestEventId.FromNewVersion7Guid(),
        RequestId = requestId,
        Type = UpdateRequestEventType.Previewed,
        ActorId = by.UserId,
        At = by.At,
        Success = success,
        DurationMs = durationMs,
        AffectedRows = affectedRows,
        ResultSetCount = resultSetCount,
        Error = error,
    };
}
