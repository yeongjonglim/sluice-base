using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Queries;

public sealed class QueryLog
{
#pragma warning disable CS8618
    private QueryLog() { }
#pragma warning restore CS8618

    public QueryLogId Id { get; private set; }
    public UserId? UserId { get; private set; }
    public ServerId? ServerId { get; private set; }
    public string QueryText { get; private set; }
    public QueryLogStatus Status { get; private set; }
    public DateTimeOffset ExecutedAt { get; private set; }
    public int? DurationMs { get; private set; }
    public int? RowCount { get; private set; }
    public string? Error { get; private set; }

    public static QueryLog Create(
        UserId? userId,
        ServerId? serverId,
        string queryText,
        QueryLogStatus status,
        DateTimeOffset executedAt,
        int? durationMs,
        int? rowCount,
        string? error) => new()
    {
        Id = QueryLogId.FromNewVersion7Guid(),
        UserId = userId,
        ServerId = serverId,
        QueryText = queryText,
        Status = status,
        ExecutedAt = executedAt,
        DurationMs = durationMs,
        RowCount = rowCount,
        Error = error,
    };
}