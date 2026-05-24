using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UserColumnBypassConfiguration : IEntityTypeConfiguration<UserColumnBypass>
{
    public void Configure(EntityTypeBuilder<UserColumnBypass> builder)
    {
        builder.ToTable("user_column_bypass");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.GrantedAt).IsRequired();
        builder.HasIndex(b => new { b.UserId, b.SensitiveColumnId }).IsUnique();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SensitiveColumn>()
            .WithMany()
            .HasForeignKey(b => b.SensitiveColumnId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
