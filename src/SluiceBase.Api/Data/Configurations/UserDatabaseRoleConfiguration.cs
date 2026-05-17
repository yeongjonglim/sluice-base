using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UserDatabaseRoleConfiguration : IEntityTypeConfiguration<UserDatabaseRole>
{
    public void Configure(EntityTypeBuilder<UserDatabaseRole> builder)
    {
        builder.ToTable("user_database_role");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Permission).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.UserId, r.Permission, r.DatabaseId }).IsUnique();
        builder.Property(r => r.GrantedAt).IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Database>()
            .WithMany()
            .HasForeignKey(r => r.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
