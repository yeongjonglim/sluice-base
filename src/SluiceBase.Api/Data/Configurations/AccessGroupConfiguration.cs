using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class AccessGroupConfiguration : IEntityTypeConfiguration<AccessGroup>
{
    public void Configure(EntityTypeBuilder<AccessGroup> builder)
    {
        builder.ToTable("access_group");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(g => g.Name).IsUnique();
        builder.Property(g => g.Description).HasMaxLength(512);
        builder.Property(g => g.CreatedAt).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.CreatedById).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccessGroupMemberConfiguration : IEntityTypeConfiguration<AccessGroupMember>
{
    public void Configure(EntityTypeBuilder<AccessGroupMember> builder)
    {
        builder.ToTable("access_group_member");
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();
        builder.Property(m => m.AddedAt).IsRequired();
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.AddedById).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccessGroupPermissionConfiguration : IEntityTypeConfiguration<AccessGroupPermission>
{
    public void Configure(EntityTypeBuilder<AccessGroupPermission> builder)
    {
        builder.ToTable("access_group_permission");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => new { p.GroupId, p.Permission }).IsUnique();
        builder.Property(p => p.GrantedAt).IsRequired();
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(p => p.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.GrantedById).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccessGroupDatabaseRoleConfiguration : IEntityTypeConfiguration<AccessGroupDatabaseRole>
{
    public void Configure(EntityTypeBuilder<AccessGroupDatabaseRole> builder)
    {
        builder.ToTable("access_group_database_role");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.GroupId, r.Permission, r.DatabaseId }).IsUnique();
        builder.Property(r => r.GrantedAt).IsRequired();
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Database>().WithMany().HasForeignKey(r => r.DatabaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.GrantedById).OnDelete(DeleteBehavior.SetNull);
    }
}
