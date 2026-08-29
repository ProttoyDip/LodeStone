using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public class RiskScoreConfiguration : IEntityTypeConfiguration<RiskScore>
{
    public void Configure(EntityTypeBuilder<RiskScore> builder)
    {
        builder.Property(score => score.CourseKey).IsRequired().HasMaxLength(120);
        builder.Property(score => score.FeatureSchemaVersion).IsRequired().HasMaxLength(64);
        builder.Property(score => score.ModelVersion).IsRequired().HasMaxLength(128);

        builder.HasIndex(score => new { score.RiskFeatureSnapshotId, score.ModelVersion })
            .HasDatabaseName("UX_RiskScores_Snapshot_Model")
            .IsUnique();
        builder.HasIndex(score => new { score.StudentProfileId, score.ScoredAtUtc });
        builder.HasIndex(score => score.RiskScoringRunId);

        builder.HasOne(score => score.StudentProfile)
            .WithMany(profile => profile.RiskScores)
            .HasForeignKey(score => score.StudentProfileId);
        builder.HasOne(score => score.RiskFeatureSnapshot)
            .WithMany(snapshot => snapshot.RiskScores)
            .HasForeignKey(score => score.RiskFeatureSnapshotId);
        builder.HasOne(score => score.RiskScoringRun)
            .WithMany(run => run.RiskScores)
            .HasForeignKey(score => score.RiskScoringRunId)
            .IsRequired(false);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RiskScores_Probability",
                "[Probability] >= 0 AND [Probability] <= 1");
            table.HasCheckConstraint(
                "CK_RiskScores_Level",
                "[Level] >= 0 AND [Level] <= 3");
        });
    }
}
