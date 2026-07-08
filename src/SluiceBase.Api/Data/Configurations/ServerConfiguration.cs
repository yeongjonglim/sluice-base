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
        builder.Property(s => s.Host).HasMaxLength(255).IsRequired();

        // Kind is computed per concrete type; the `kind` column is the TPH discriminator.
        builder.Ignore(s => s.Kind);
        builder.HasDiscriminator<string>("kind")
            .HasValue<PostgresServer>("postgres")
            .HasValue<MongoServer>("mongodb");
        builder.Property<string>("kind").HasMaxLength(32);
    }
}

// MongoServer-only columns. Under TPH these are nullable in the shared `server` table
// (PostgreSQL rows leave them null); MongoServer instances always carry a value.
internal sealed class MongoServerConfiguration : IEntityTypeConfiguration<MongoServer>
{
    public void Configure(EntityTypeBuilder<MongoServer> builder)
    {
        builder.Property(s => s.ConnectionMode)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(s => s.AuthSource).HasMaxLength(128);
        builder.Property(s => s.ReplicaSet).HasMaxLength(128);
    }
}
