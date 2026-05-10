using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class DatabaseConfiguration : IEntityTypeConfiguration<Database>
{
    public void Configure(EntityTypeBuilder<Database> builder)
    {
        builder.ToTable("server_database");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(d => d.DatabaseName).HasMaxLength(255).IsRequired();
        builder.HasOne(d => d.Server)
            .WithMany(s => s.Databases)
            .HasForeignKey(d => d.ServerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Credential>()
            .WithMany()
            .HasForeignKey(d => d.ReadCredentialId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Credential>()
            .WithMany()
            .HasForeignKey(d => d.WriteCredentialId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}