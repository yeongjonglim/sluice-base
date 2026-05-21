using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class SensitiveColumnEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/database/{databaseId}/sensitive-column", ListByDatabase)
            .WithName("ListSensitiveColumns");
        admin.MapPost("/database/{databaseId}/sensitive-column", MarkColumn)
            .WithName("MarkSensitiveColumn");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}", UnmarkColumn)
            .WithName("UnmarkSensitiveColumn");

        admin.MapPost("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass", GrantBypass)
            .WithName("GrantColumnBypass");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/{userId}", RevokeBypass)
            .WithName("RevokeColumnBypass");
    }

    // ── list ──────────────────────────────────────────────────────────────────

    private static async Task<Ok<SensitiveColumnListResponse>> ListByDatabase(
        DatabaseId databaseId, AppDbContext db, CancellationToken ct)
    {
        var columns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == databaseId)
            .ToListAsync(ct);

        var columnIds = columns.Select(c => c.Id).ToHashSet();

        var rawBypasses = await db.UserColumnBypasses
            .AsNoTracking()
            .Where(b => columnIds.Contains(b.SensitiveColumnId))
            .Join(db.ExternalLogins,
                b => b.UserId, l => l.UserId,
                (b, l) => new { b.Id, b.UserId, l.Email, l.Name, b.GrantedAt, b.GrantedById, b.SensitiveColumnId })
            .ToListAsync(ct);

        var bypassesBySensitiveColumnId = rawBypasses
            .GroupBy(b => b.SensitiveColumnId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<BypassItem>)g.Select(b =>
                    new BypassItem(b.Id, b.UserId, b.Email, b.Name, b.GrantedAt, b.GrantedById)).ToList());

        var items = columns.Select(c => new SensitiveColumnItem(
            c.Id, c.SchemaName, c.TableName, c.ColumnName, c.MarkedAt, c.MarkedById,
            bypassesBySensitiveColumnId.TryGetValue(c.Id, out var bs) ? bs : []));

        return TypedResults.Ok(new SensitiveColumnListResponse([.. items]));
    }

    // ── mark / unmark ─────────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> MarkColumn(
        DatabaseId databaseId,
        MarkSensitiveColumnRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var dbExists = await db.Databases.AnyAsync(d => d.Id == databaseId, ct);
        if (!dbExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.SensitiveColumns.AnyAsync(
            c => c.DatabaseId == databaseId
              && c.SchemaName == req.SchemaName
              && c.TableName == req.TableName
              && c.ColumnName == req.ColumnName, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.SensitiveColumns.Add(SensitiveColumn.Mark(
            databaseId, req.SchemaName, req.TableName, req.ColumnName,
            actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/database/{databaseId}/sensitive-column");
    }

    private static async Task<NoContent> UnmarkColumn(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        AppDbContext db,
        CancellationToken ct)
    {
        var column = await db.SensitiveColumns.SingleOrDefaultAsync(
            c => c.DatabaseId == databaseId && c.Id == sensitiveColumnId, ct);
        if (column is not null)
        {
            db.SensitiveColumns.Remove(column);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    // ── bypass ────────────────────────────────────────────────────────────────

    private static async Task<Results<NotFound, Ok, Created>> GrantBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        GrantBypassRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var columnExists = await db.SensitiveColumns.AnyAsync(
            c => c.Id == sensitiveColumnId && c.DatabaseId == databaseId, ct);
        if (!columnExists)
        {
            return TypedResults.NotFound();
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == req.UserId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.UserColumnBypasses.AnyAsync(
            b => b.UserId == req.UserId && b.SensitiveColumnId == sensitiveColumnId, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.UserColumnBypasses.Add(UserColumnBypass.Grant(
            req.UserId, sensitiveColumnId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass");
    }

    private static async Task<NoContent> RevokeBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        UserId userId,
        AppDbContext db,
        CancellationToken ct)
    {
        var bypass = await db.UserColumnBypasses.SingleOrDefaultAsync(
            b => b.SensitiveColumnId == sensitiveColumnId && b.UserId == userId, ct);
        if (bypass is not null)
        {
            db.UserColumnBypasses.Remove(bypass);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    // ── request / response records ────────────────────────────────────────────

    public sealed record MarkSensitiveColumnRequest(string SchemaName, string TableName, string ColumnName);
    public sealed record GrantBypassRequest(UserId UserId);

    public sealed record BypassItem(
        UserColumnBypassId Id,
        UserId UserId,
        string? UserEmail,
        string? UserName,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record SensitiveColumnItem(
        SensitiveColumnId Id,
        string SchemaName,
        string TableName,
        string ColumnName,
        DateTimeOffset MarkedAt,
        UserId? MarkedById,
        IReadOnlyList<BypassItem> Bypasses);

    public sealed record SensitiveColumnListResponse(IReadOnlyList<SensitiveColumnItem> Columns);
}
