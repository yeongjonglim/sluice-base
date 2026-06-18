using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Mcp;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class McpAuthCodeConfiguration : IEntityTypeConfiguration<McpAuthCode>
{
    public void Configure(EntityTypeBuilder<McpAuthCode> builder)
    {
        builder.ToTable("mcp_auth_code");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CodeHash).IsRequired();
        builder.Property(c => c.ClientId).IsRequired();
        builder.Property(c => c.RedirectUri).IsRequired();
        builder.Property(c => c.CodeChallenge).IsRequired();
        builder.HasIndex(c => c.CodeHash).IsUnique();

        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
