using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class ServerConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> builder)
    {
        builder.ToTable("server");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique().HasFilter("deleted_at IS NULL");
        builder.Property(s => s.Kind).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Host).HasMaxLength(255).IsRequired();
        builder.Property(s => s.ConnectionMode)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(ConnectionMode.Standard)
            .IsRequired();
        builder.Property(s => s.AuthSource).HasMaxLength(128);
        builder.Property(s => s.ReplicaSet).HasMaxLength(128);
        builder.Property(s => s.UseTls).IsRequired();
    }
}