using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace IntegrationTests;

public class EffectiveAccessTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    [Fact]
    public async Task Member_CanQueryDatabase_GrantedOnlyViaGroup()
    {
        var ct = TestContext.Current.CancellationToken;

        // alice signs in; resolve her user id
        var alice = await LoginHelper.SignInAsync("alice", "dev", ct);

        // Capture the seeded DatabaseId outside the scope so it's accessible after the context closes.
        DatabaseId? seededDatabaseId = null;

        // Seed a database + a group that grants query:execute on it, with alice as member.
        await using (var db = await AccessGroupTestHelper.CreateMetadataDbContextAsync(factory, ct))
        {
            var aliceUser = await AccessGroupTestHelper.GetUserByEmailAsync(db, "alice@example.com", ct);
            var (_, dbId) = await AccessGroupTestHelper.SeedDatabaseOnlyAsync(db, ct);

            seededDatabaseId = dbId;

            var group = AccessGroup.Create($"grp-{Guid.NewGuid():N}"[..16], null, null, DateTimeOffset.UtcNow);
            db.AccessGroups.Add(group);
            db.AccessGroupMembers.Add(AccessGroupMember.Add(group.Id, aliceUser.Id, null, DateTimeOffset.UtcNow));
            db.AccessGroupDatabaseRoles.Add(
                AccessGroupDatabaseRole.Grant(group.Id, Permissions.QueryExecute, dbId, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
        }

        // alice executes a query on that database — allowed purely via the group grant.
        // The query may fail to *run* against a non-real database, but it must not return
        // 403 Forbidden — authorization is what we assert here.
        var resp = await alice.Client.PostAsJsonAsync("/api/query",
            new { databaseId = seededDatabaseId!.Value.Value.ToString(), sql = "SELECT 1" }, ct);

        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
