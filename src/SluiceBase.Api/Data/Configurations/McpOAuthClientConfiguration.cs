using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class McpOAuthClientConfiguration : IEntityTypeConfiguration<McpOAuthClient>
{
    public void Configure(EntityTypeBuilder<McpOAuthClient> builder)
    {
        builder.ToTable("mcp_oauth_client");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ClientId).IsRequired();
        builder.Property(c => c.ClientName).IsRequired();
        builder.Property(c => c.RedirectUris).HasColumnType("jsonb");
        builder.HasIndex(c => c.ClientId).IsUnique();
    }
}
