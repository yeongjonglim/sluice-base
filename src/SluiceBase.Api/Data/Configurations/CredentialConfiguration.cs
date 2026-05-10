using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class CredentialConfiguration : IEntityTypeConfiguration<Credential>
{
    public void Configure(EntityTypeBuilder<Credential> builder)
    {
        builder.ToTable("server_credential");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Label).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Username).HasMaxLength(128).IsRequired();
        builder.Property(c => c.EncryptedPassword).HasMaxLength(4096).IsRequired();
        builder.HasOne<Server>()
            .WithMany(s => s.Credentials)
            .HasForeignKey(c => c.ServerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}