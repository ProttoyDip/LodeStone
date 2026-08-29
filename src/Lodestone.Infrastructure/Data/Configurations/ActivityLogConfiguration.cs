using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.HasIndex(item => new { item.StudentProfileId, item.OccurredAtUtc });
        builder.HasOne(item => item.StudentProfile)
            .WithMany(profile => profile.ActivityLogs)
            .HasForeignKey(item => item.StudentProfileId);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ActivityLogs_NonNegativeCounts",
            "[LoginCount] >= 0 AND [ForumInteractions] >= 0 AND [CourseInteractions] >= 0 AND [DaysSinceLastAccess] >= 0 AND [AssignmentsLateCount] >= 0"));
    }
}
