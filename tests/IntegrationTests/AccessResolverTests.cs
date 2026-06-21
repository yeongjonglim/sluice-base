using IntegrationTests.Supports;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace IntegrationTests;

public class AccessResolverTests(SluiceBaseStackFactory factory)
{
    // Seeds: a user, a server+database, optionally direct and/or group grants.
    // Returns the user id and database id. See AccessGroupTestHelper for seeding utilities.
    private async Task<(UserId User, DatabaseId Db)> SeedAsync(
        Func<AppDbContext, UserId, DatabaseId, Task> arrange, CancellationToken ct)
    {
        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        var (userId, dbId) = await AccessGroupTestHelper.SeedUserAndDatabaseAsync(db, ct);
        await arrange(db, userId, dbId);
        await db.SaveChangesAsync(ct);
        return (userId, dbId);
    }

    private static AccessResolver ResolverOver(AppDbContext db) => new(db);

    [Fact]
    public async Task HasDatabasePermission_TrueForDirectGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync((db, u, d) =>
        {
            db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(u, Permissions.QueryExecute, d, null, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        Assert.True(await ResolverOver(db).HasDatabasePermissionAsync(user, dbId, Permissions.QueryExecute, ct));
    }

    [Fact]
    public async Task HasDatabasePermission_TrueForGroupGrant_ViaMembership()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync(async (db, u, d) =>
        {
            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryExecute, d, null, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        Assert.True(await ResolverOver(db).HasDatabasePermissionAsync(user, dbId, Permissions.QueryExecute, ct));
    }

    [Fact]
    public async Task HasDatabasePermission_FalseWhenGroupGrantButNotMember()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync((db, u, d) =>
        {
            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            // grant to group but DO NOT add user as member
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryExecute, d, null, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        Assert.False(await ResolverOver(db).HasDatabasePermissionAsync(user, dbId, Permissions.QueryExecute, ct));
    }

    [Fact]
    public async Task DatabasesWithPermission_UnionsDirectAndGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync(async (db, u, d) =>
        {
            db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(u, Permissions.QueryAudit, d, null, DateTimeOffset.UtcNow));
            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryAudit, d, null, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        var result = await ResolverOver(db).DatabasesWithPermissionAsync(user, Permissions.QueryAudit, ct);
        Assert.Contains(dbId, result);
        Assert.Single(result); // de-duplicated across direct + group
    }

    [Fact]
    public async Task HasGlobalPermission_TrueForGroupGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, _) = await SeedAsync((db, u, d) =>
        {
            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupPermissions.Add(
                AccessGroupPermission.Grant(group.Id, Permissions.ServerManage, null, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        Assert.True(await ResolverOver(db).HasGlobalPermissionAsync(user, Permissions.ServerManage, ct));
    }

    [Fact]
    public async Task DatabasesWithAnyScopeable_UnionsDirectAndGroup_Deduplicated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync(async (db, u, d) =>
        {
            // direct submit role + group execute role on the SAME database
            db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(u, Permissions.UpdateSubmit, d, null, DateTimeOffset.UtcNow));
            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.UpdateExecute, d, null, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        var result = await ResolverOver(db).DatabasesWithAnyScopeableAsync(user, ct);
        Assert.Contains(dbId, result);
        Assert.Single(result); // two roles on one database collapse to a single id
    }

    [Fact]
    public async Task EffectivePermissions_IncludesDirectAndGroupGlobalAndScopeable()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, _) = await SeedAsync(async (db, u, d) =>
        {
            db.UserPermissions.Add(UserPermissionMap.Grant(u, Permissions.ServerManage, null, DateTimeOffset.UtcNow));
            db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(u, Permissions.QueryExecute, d, null, DateTimeOffset.UtcNow));
            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupPermissions.Add(
                AccessGroupPermission.Grant(group.Id, Permissions.PermissionManage, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryAudit, d, null, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        var result = await ResolverOver(db).EffectivePermissionsAsync(user, ct);
        Assert.Contains(Permissions.ServerManage, result);     // direct global
        Assert.Contains(Permissions.QueryExecute, result);     // direct scopeable
        Assert.Contains(Permissions.PermissionManage, result); // group global
        Assert.Contains(Permissions.QueryAudit, result);       // group scopeable
    }

    [Fact]
    public async Task HasAnyDatabasePermission_TrueWhenOneOfSeveralHeldViaGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, dbId) = await SeedAsync(async (db, u, d) =>
        {
            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}", null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, u, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.UpdateApprove, d, null, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }, ct);

        await using var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct);
        var resolver = ResolverOver(db);
        Assert.True(await resolver.HasAnyDatabasePermissionAsync(
            user, dbId,
            [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute], ct));
        Assert.False(await resolver.HasAnyDatabasePermissionAsync(
            user, dbId,
            [Permissions.UpdateSubmit, Permissions.UpdateExecute], ct));
    }
}
