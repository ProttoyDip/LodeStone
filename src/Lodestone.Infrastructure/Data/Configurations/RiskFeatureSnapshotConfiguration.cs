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
                "[ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0");
        });
    }
}
