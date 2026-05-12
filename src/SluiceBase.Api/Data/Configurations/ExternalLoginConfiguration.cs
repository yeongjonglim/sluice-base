using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

public class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("external_login");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id);

        builder.HasIndex(x => new { x.Issuer, x.Subject });
        builder.Property(x => x.Issuer);
        builder.Property(x => x.Subject);
        builder.Property(x => x.Name);
        builder.Property(x => x.Email);
        builder.Property(x => x.CreatedAt);
        builder.Property(x => x.LastLoginAt);
        builder.OwnsMany(x => x.Claims,
            x =>
            {
                x.ToJson();
            });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}