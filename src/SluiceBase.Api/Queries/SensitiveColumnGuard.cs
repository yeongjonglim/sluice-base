using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Queries;

// Result of evaluating a SQL string against a user's sensitive-column policy.
// BlockedHits empty => allowed. Touched is the full set of sensitive columns the
// SQL references (for audit), regardless of the user's bypass grants.
internal sealed record SensitiveColumnDecision(
    IReadOnlyList<SensitiveColumnHit> BlockedHits,
    IReadOnlyList<string> Touched);

internal sealed class SensitiveColumnGuard(AppDbContext db)
{
    public async Task<SensitiveColumnDecision> EvaluateAsync(
        UserId userId, DatabaseId databaseId, string sql, CancellationToken ct)
    {
        var sensitiveColumns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == databaseId)
            .ToListAsync(ct);

        if (sensitiveColumns.Count == 0)
        {
            return new SensitiveColumnDecision([], []);
        }

        var allSensitive = sensitiveColumns
            .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
            .ToList();
        var allHits = SqlColumnChecker.FindBlockedColumns(sql, allSensitive);

        if (allHits.Count == 0)
        {
            return new SensitiveColumnDecision([], []);
        }

        var touched = allHits
            .Select(h => $"{h.Schema}.{h.Table}.{h.Column}")
            .ToArray();

        var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
        var bypassedIds = await db.UserColumnBypasses
            .AsNoTracking()
            .Where(b => b.UserId == userId && sensitiveColumnIds.Contains(b.SensitiveColumnId))
            .Select(b => b.SensitiveColumnId)
            .ToListAsync(ct);

        var blocked = sensitiveColumns
            .Where(c => !bypassedIds.Contains(c.Id))
            .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
            .ToList();

        var blockedHits = blocked.Count == 0
            ? []
            : SqlColumnChecker.FindBlockedColumns(sql, blocked);

        return new SensitiveColumnDecision(blockedHits, touched);
    }
}
