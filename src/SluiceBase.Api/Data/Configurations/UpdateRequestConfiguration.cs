// src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Updates;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UpdateRequestConfiguration : IEntityTypeConfiguration<UpdateRequest>
{
    public void Configure(EntityTypeBuilder<UpdateRequest> builder)
    {
        builder.ToTable("update_request");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.SqlText).IsRequired();
        builder.Property(r => r.Reason).IsRequired();
        // Every transition saves with "WHERE status = <status it was loaded with>", so two concurrent
        // transitions (two executes, or an execute and a cancel) cannot both succeed.
        builder.Property(r => r.Status).HasMaxLength(16).IsRequired().IsConcurrencyToken();

        builder.HasOne(r => r.Database).WithMany()
            .HasForeignKey(r => r.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Submitter).WithMany()
            .HasForeignKey(r => r.SubmitterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.Reviewer).WithMany()
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.Executor).WithMany()
            .HasForeignKey(r => r.ExecutorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.CancelledBy).WithMany()
            .HasForeignKey(r => r.CancelledById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.SourceRequest).WithMany()
            .HasForeignKey(r => r.SourceRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}