using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class RiskFeatureSnapshotConfiguration : IEntityTypeConfiguration<RiskFeatureSnapshot>
{
    public void Configure(EntityTypeBuilder<RiskFeatureSnapshot> builder)
    {
        builder.Property(snapshot => snapshot.CourseKey).IsRequired().HasMaxLength(120);
        builder.Property(snapshot => snapshot.FeatureSchemaVersion).IsRequired().HasMaxLength(64);
        builder.Property(snapshot => snapshot.SourceFileName).IsRequired().HasMaxLength(260);
        builder.Property(snapshot => snapshot.SourceFileSha256)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(snapshot => snapshot.RecentActiveDayRate).HasColumnType("real");
        builder.Property(snapshot => snapshot.PriorActiveDayRate).HasColumnType("real");
        builder.Property(snapshot => snapshot.ActiveDayRateTrend).HasColumnType("real");
        builder.Property(snapshot => snapshot.RecentCourseClickRate).HasColumnType("real");
        builder.Property(snapshot => snapshot.PriorCourseClickRate).HasColumnType("real");
        builder.Property(snapshot => snapshot.CourseClickRateTrend).HasColumnType("real");
        builder.Property(snapshot => snapshot.InactivityStreakDays).HasColumnType("real");
        builder.Property(snapshot => snapshot.AssessmentDueRate).HasColumnType("real");
        builder.Property(snapshot => snapshot.AssessmentOnTimeRate).HasColumnType("real");
        builder.Property(snapshot => snapshot.AssessmentLateOrMissingRate).HasColumnType("real");
        builder.Property(snapshot => snapshot.CourseProgressRatio).HasColumnType("real");
        builder.Property(snapshot => snapshot.CohortActivityPercentile).HasColumnType("real");

        builder.HasIndex(snapshot => new
            {
                snapshot.StudentProfileId,
                snapshot.CourseKey,
                snapshot.WindowEndUtc,
                snapshot.FeatureSchemaVersion
            })
            .HasDatabaseName("UX_RiskFeatureSnapshots_Student_Course_Window_Schema")
            .IsUnique();
        builder.HasIndex(snapshot => new { snapshot.FeatureSchemaVersion, snapshot.WindowEndUtc });

        builder.HasOne(snapshot => snapshot.StudentProfile)
            .WithMany(profile => profile.RiskFeatureSnapshots)
            .HasForeignKey(snapshot => snapshot.StudentProfileId);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RiskFeatureSnapshots_ObservedDays",
                "[ObservedDays] > 0 AND [ObservedDays] <= 365");
            table.HasCheckConstraint(
                "CK_RiskFeatureSnapshots_Features",
                "([FeatureSchemaVersion] = 'withdrawal-28d-v1' AND [ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0) OR ([FeatureSchemaVersion] = 'withdrawal-28d-v2' AND [RecentActiveDayRate] IS NOT NULL AND [PriorActiveDayRate] IS NOT NULL AND [ActiveDayRateTrend] IS NOT NULL AND [RecentCourseClickRate] IS NOT NULL AND [PriorCourseClickRate] IS NOT NULL AND [CourseClickRateTrend] IS NOT NULL AND [InactivityStreakDays] IS NOT NULL AND [AssessmentDueRate] IS NOT NULL AND [AssessmentOnTimeRate] IS NOT NULL AND [AssessmentLateOrMissingRate] IS NOT NULL AND [CourseProgressRatio] IS NOT NULL AND [CohortActivityPercentile] IS NOT NULL AND [RecentActiveDayRate] BETWEEN 0 AND 1 AND [PriorActiveDayRate] BETWEEN 0 AND 1 AND [ActiveDayRateTrend] BETWEEN -1 AND 1 AND [RecentCourseClickRate] >= 0 AND [PriorCourseClickRate] >= 0 AND [InactivityStreakDays] BETWEEN 0 AND [ObservedDays] AND [AssessmentDueRate] >= 0 AND [AssessmentOnTimeRate] BETWEEN 0 AND 1 AND [AssessmentLateOrMissingRate] BETWEEN 0 AND 1 AND [CourseProgressRatio] BETWEEN 0 AND 1 AND [CohortActivityPercentile] BETWEEN 0 AND 1)");
        });
    }
}
