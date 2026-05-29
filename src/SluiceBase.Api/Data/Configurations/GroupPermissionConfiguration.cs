using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupPermissionConfiguration : IEntityTypeConfiguration<GroupPermissionMap>
{
    public void Configure(EntityTypeBuilder<GroupPermissionMap> builder)
    {
        builder.ToTable("group_permission_map");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => new { p.GroupId, p.Permission }).IsUnique();
        builder.Property(p => p.GrantedAt).IsRequired();
        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
