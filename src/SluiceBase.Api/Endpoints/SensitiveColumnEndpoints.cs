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
            .RequireAuthorization();

        admin.MapGet("/database/{databaseId}/sensitive-column", ListByDatabase)
            .WithName("ListSensitiveColumns");
        admin.MapPost("/database/{databaseId}/sensitive-column", MarkColumn)
            .WithName("MarkSensitiveColumn");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}", UnmarkColumn)
            .WithName("UnmarkSensitiveColumn");

        admin.MapPost("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass", GrantBypass)
            .WithName("GrantColumnBypass");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/user/{userId}", RevokeUserBypass)
            .WithName("RevokeUserColumnBypass");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/group/{groupId}", RevokeGroupBypass)
            .WithName("RevokeGroupColumnBypass");
    }

    // ── list ──────────────────────────────────────────────────────────────────

    private static async Task<Results<ForbidHttpResult, Ok<SensitiveColumnListResponse>>> ListByDatabase(
        DatabaseId databaseId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null || (!user.HasPermission(Permissions.PermissionManage) && !user.HasPermission(Permissions.GroupManage)))
        {
            return TypedResults.Forbid();
        }

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

        var rawGroupBypasses = await db.GroupColumnBypasses
            .AsNoTracking()
            .Where(b => columnIds.Contains(b.SensitiveColumnId))
            .Join(db.Groups,
                b => b.GroupId, g => g.Id,
                (b, g) => new { b.Id, b.GroupId, g.Name, b.GrantedAt, b.GrantedById, b.SensitiveColumnId })
            .ToListAsync(ct);

        var groupBypassesBySensitiveColumnId = rawGroupBypasses
            .GroupBy(b => b.SensitiveColumnId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<GroupBypassItem>)g.Select(b =>
                    new GroupBypassItem(b.Id, b.GroupId, b.Name, b.GrantedAt, b.GrantedById)).ToList());

        var items = columns.Select(c => new SensitiveColumnItem(
            c.Id, c.SchemaName, c.TableName, c.ColumnName, c.MarkedAt, c.MarkedById,
            bypassesBySensitiveColumnId.TryGetValue(c.Id, out var bs) ? bs : [],
            groupBypassesBySensitiveColumnId.TryGetValue(c.Id, out var gbs) ? gbs : []));

        return TypedResults.Ok(new SensitiveColumnListResponse([.. items]));
    }

    // ── mark / unmark ─────────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created, ForbidHttpResult>> MarkColumn(
        DatabaseId databaseId,
        MarkSensitiveColumnRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null || !user.HasPermission(Permissions.PermissionManage))
        {
            return TypedResults.Forbid();
        }

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

        db.SensitiveColumns.Add(SensitiveColumn.Mark(
            databaseId, req.SchemaName, req.TableName, req.ColumnName,
            user.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/database/{databaseId}/sensitive-column");
    }

    private static async Task<Results<NoContent, ForbidHttpResult>> UnmarkColumn(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null || !user.HasPermission(Permissions.PermissionManage))
        {
            return TypedResults.Forbid();
        }

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

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created, ForbidHttpResult>> GrantBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        GrantBypassRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if ((req.UserId is null && req.GroupId is null) || (req.UserId is not null && req.GroupId is not null))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Exactly one of 'userId' or 'groupId' must be provided."]
            });
        }

        var user = await currentUser.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Forbid();
        }

        if (req.UserId is not null && !user.HasPermission(Permissions.PermissionManage))
        {
            return TypedResults.Forbid();
        }

        if (req.GroupId is not null && !user.HasPermission(Permissions.GroupManage))
        {
            return TypedResults.Forbid();
        }

        var columnExists = await db.SensitiveColumns.AnyAsync(
            c => c.Id == sensitiveColumnId && c.DatabaseId == databaseId, ct);
        if (!columnExists)
        {
            return TypedResults.NotFound();
        }

        var actor = await currentUser.GetAsync(ct);

        if (req.UserId is { } userId)
        {
            var userExists = await db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!userExists)
            {
                return TypedResults.NotFound();
            }

            var existing = await db.UserColumnBypasses.AnyAsync(
                b => b.UserId == userId && b.SensitiveColumnId == sensitiveColumnId, ct);
            if (existing)
            {
                return TypedResults.Ok();
            }

            db.UserColumnBypasses.Add(UserColumnBypass.Grant(
                userId, sensitiveColumnId, actor?.Id, clock.GetUtcNow()));
        }
        else
        {
            var groupId = req.GroupId!.Value;
            var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
            if (!groupExists)
            {
                return TypedResults.NotFound();
            }

            var existing = await db.GroupColumnBypasses.AnyAsync(
                b => b.GroupId == groupId && b.SensitiveColumnId == sensitiveColumnId, ct);
            if (existing)
            {
                return TypedResults.Ok();
            }

            db.GroupColumnBypasses.Add(GroupColumnBypass.Grant(
                groupId, sensitiveColumnId, actor?.Id, clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Created(
            $"/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass");
    }

    private static async Task<Results<NoContent, ForbidHttpResult>> RevokeUserBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        UserId userId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null || !user.HasPermission(Permissions.PermissionManage))
        {
            return TypedResults.Forbid();
        }

        var bypass = await db.UserColumnBypasses.SingleOrDefaultAsync(
            b => b.SensitiveColumnId == sensitiveColumnId && b.UserId == userId, ct);
        if (bypass is not null)
        {
            db.UserColumnBypasses.Remove(bypass);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ForbidHttpResult>> RevokeGroupBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        GroupId groupId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null || !user.HasPermission(Permissions.GroupManage))
        {
            return TypedResults.Forbid();
        }

        var bypass = await db.GroupColumnBypasses.SingleOrDefaultAsync(
            b => b.SensitiveColumnId == sensitiveColumnId && b.GroupId == groupId, ct);
        if (bypass is not null)
        {
            db.GroupColumnBypasses.Remove(bypass);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    // ── request / response records ────────────────────────────────────────────

    public sealed record MarkSensitiveColumnRequest(string SchemaName, string TableName, string ColumnName);
    public sealed record GrantBypassRequest(UserId? UserId, GroupId? GroupId);

    public sealed record BypassItem(
        UserColumnBypassId Id,
        UserId UserId,
        string? UserEmail,
        string? UserName,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record GroupBypassItem(
        GroupColumnBypassId Id,
        GroupId GroupId,
        string? GroupName,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record SensitiveColumnItem(
        SensitiveColumnId Id,
        string SchemaName,
        string TableName,
        string ColumnName,
        DateTimeOffset MarkedAt,
        UserId? MarkedById,
        IReadOnlyList<BypassItem> Bypasses,
        IReadOnlyList<GroupBypassItem> GroupBypasses);

    public sealed record SensitiveColumnListResponse(IReadOnlyList<SensitiveColumnItem> Columns);
}
