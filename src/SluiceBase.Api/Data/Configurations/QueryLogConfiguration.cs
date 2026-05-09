using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class QueryLogConfiguration : IEntityTypeConfiguration<QueryLog>
{
    public void Configure(EntityTypeBuilder<QueryLog> builder)
    {
        builder.ToTable("query_log");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.QueryText).IsRequired();
        builder.Property(q => q.Status).HasMaxLength(16).IsRequired();

        builder.HasOne<User>().WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Server>().WithMany()
            .HasForeignKey(q => q.ServerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}