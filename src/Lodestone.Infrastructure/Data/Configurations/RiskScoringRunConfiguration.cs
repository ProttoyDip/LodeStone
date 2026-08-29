using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class RiskScoringRunConfiguration : IEntityTypeConfiguration<RiskScoringRun>
{
    public void Configure(EntityTypeBuilder<RiskScoringRun> builder)
    {
        builder.Property(run => run.ModelVersion).IsRequired().HasMaxLength(128);
        builder.Property(run => run.FeatureSchemaVersion).IsRequired().HasMaxLength(64);
        builder.Property(run => run.FailureSummary).HasMaxLength(2_000);
        builder.HasIndex(run => run.RunKey).IsUnique();
        builder.HasIndex(run => run.StartedAtUtc);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RiskScoringRuns_Status",
                "[Status] >= 0 AND [Status] <= 4");
            table.HasCheckConstraint(
                "CK_RiskScoringRuns_Counts",
                "[CandidateCount] >= 0 AND [ScoredCount] >= 0 AND [SkippedCount] >= 0 AND [FailedCount] >= 0 AND [QueueCreatedCount] >= 0 AND [QueueEscalatedCount] >= 0");
        });
    }
}
