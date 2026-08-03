// src/SluiceBase.Api/Data/Configurations/UpdateRequestEventConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Updates;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UpdateRequestEventConfiguration : IEntityTypeConfiguration<UpdateRequestEvent>
{
    public void Configure(EntityTypeBuilder<UpdateRequestEvent> builder)
    {
        builder.ToTable("update_request_event");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasMaxLength(16).IsRequired();

        // Events are meaningless without their request — cascade delete with it.
        builder.HasOne(e => e.Request).WithMany(r => r.Events)
            .HasForeignKey(e => e.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Actor).WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.RequestId);
    }
}
