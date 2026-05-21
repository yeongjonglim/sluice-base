using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class SensitiveColumnConfiguration : IEntityTypeConfiguration<SensitiveColumn>
{
    public void Configure(EntityTypeBuilder<SensitiveColumn> builder)
    {
        builder.ToTable("sensitive_column");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchemaName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.TableName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.ColumnName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.MarkedAt).IsRequired();
        builder.HasIndex(c => new { c.DatabaseId, c.SchemaName, c.TableName, c.ColumnName }).IsUnique();
        builder.HasOne<Database>()
            .WithMany()
            .HasForeignKey(c => c.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.MarkedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
