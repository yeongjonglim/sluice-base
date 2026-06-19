using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;
using static SluiceBase.Core.Permissions.Permissions;

namespace SluiceBase.Core.Tests;

public class AccessGroupTests
{
    [Fact]
    public void Create_TrimsName_AndSetsFields()
    {
        var actor = UserId.From(Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        var group = AccessGroup.Create("  Analysts  ", "  read access  ", actor, at);

        Assert.Equal("Analysts", group.Name);
        Assert.Equal("read access", group.Description);
        Assert.Equal(actor, group.CreatedById);
        Assert.Equal(at, group.CreatedAt);
        Assert.NotEqual(Guid.Empty, group.Id.Value);
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccessGroup.Create("   ", null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rename_UpdatesTrimmedName()
    {
        var group = AccessGroup.Create("Old", null, null, DateTimeOffset.UtcNow);
        group.Rename("  New  ");
        Assert.Equal("New", group.Name);
    }

    [Fact]
    public void SetDescription_NormalizesBlankToNull()
    {
        var group = AccessGroup.Create("G", "x", null, DateTimeOffset.UtcNow);
        group.SetDescription("   ");
        Assert.Null(group.Description);
    }

    [Fact]
    public void Member_Add_SetsFields()
    {
        var groupId = AccessGroupId.FromNewVersion7Guid();
        var userId = UserId.From(Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        var member = AccessGroupMember.Add(groupId, userId, null, at);

        Assert.Equal(groupId, member.GroupId);
        Assert.Equal(userId, member.UserId);
        Assert.Equal(at, member.AddedAt);
    }

    [Fact]
    public void DatabaseRole_Grant_SetsFields()
    {
        var groupId = AccessGroupId.FromNewVersion7Guid();
        var dbId = DatabaseId.From(Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        var role = AccessGroupDatabaseRole.Grant(groupId, QueryExecute, dbId, null, at);

        Assert.Equal(groupId, role.GroupId);
        Assert.Equal(QueryExecute, role.Permission);
        Assert.Equal(dbId, role.DatabaseId);
    }
}
