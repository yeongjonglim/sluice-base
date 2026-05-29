using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class GroupColumnBypassConfiguration : IEntityTypeConfiguration<GroupColumnBypass>
{
    public void Configure(EntityTypeBuilder<GroupColumnBypass> builder)
    {
        builder.ToTable("group_column_bypass");
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.GroupId, b.SensitiveColumnId }).IsUnique();
        builder.Property(b => b.GrantedAt).IsRequired();
        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(b => b.GroupId)
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
