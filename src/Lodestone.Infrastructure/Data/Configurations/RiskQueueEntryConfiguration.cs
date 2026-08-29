using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public class RiskQueueEntryConfiguration : IEntityTypeConfiguration<RiskQueueEntry>
{
    public void Configure(EntityTypeBuilder<RiskQueueEntry> builder)
    {
        builder.Property(entry => entry.ResolvedByUserId).HasMaxLength(450);
        builder.Property(entry => entry.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(entry => entry.StudentProfileId)
            .HasDatabaseName("UX_RiskQueueEntries_OneOpenPerStudent")
            .HasFilter("[IsResolved] = 0")
            .IsUnique();
        builder.HasIndex(entry => new { entry.IsResolved, entry.Level, entry.LastSignaledAtUtc });

        builder.HasOne(entry => entry.StudentProfile)
            .WithMany(profile => profile.RiskQueueEntries)
            .HasForeignKey(entry => entry.StudentProfileId);
        builder.HasOne(entry => entry.RiskScore)
            .WithMany()
            .HasForeignKey(entry => entry.RiskScoreId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entry => entry.TriggerRiskScore)
            .WithMany()
            .HasForeignKey(entry => entry.TriggerRiskScoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RiskQueueEntries_Level",
                "[Level] >= 0 AND [Level] <= 3");
            table.HasCheckConstraint(
                "CK_RiskQueueEntries_Resolution",
                "([IsResolved] = 0 AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([IsResolved] = 1 AND [ResolvedAtUtc] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL)");
        });
    }
}
