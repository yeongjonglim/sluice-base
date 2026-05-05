using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermissionMap>
{
    public void Configure(EntityTypeBuilder<UserPermissionMap> builder)
    {
        builder.ToTable("user_permission");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id);
        builder.Property(p => p.UserId);
        builder.Property(p => p.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => new { p.UserId, p.Permission }).IsUnique();
        builder.Property(p => p.GrantedAt).IsRequired();
        builder.Property(p => p.GrantedById);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}