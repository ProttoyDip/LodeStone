using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodestone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeMlV3Snapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots");

            migrationBuilder.AddColumn<float>(
                name: "ActivityTrendAcceleration",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "AssessmentMissStreak",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ClickVolatility",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ForumEngagementShare",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "InactiveWeekRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots",
                sql: "([FeatureSchemaVersion] = 'withdrawal-28d-v1' AND [ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0) OR ([FeatureSchemaVersion] = 'withdrawal-28d-v2' AND [RecentActiveDayRate] IS NOT NULL AND [PriorActiveDayRate] IS NOT NULL AND [ActiveDayRateTrend] IS NOT NULL AND [RecentCourseClickRate] IS NOT NULL AND [PriorCourseClickRate] IS NOT NULL AND [CourseClickRateTrend] IS NOT NULL AND [InactivityStreakDays] IS NOT NULL AND [AssessmentDueRate] IS NOT NULL AND [AssessmentOnTimeRate] IS NOT NULL AND [AssessmentLateOrMissingRate] IS NOT NULL AND [CourseProgressRatio] IS NOT NULL AND [CohortActivityPercentile] IS NOT NULL AND [RecentActiveDayRate] BETWEEN 0 AND 1 AND [PriorActiveDayRate] BETWEEN 0 AND 1 AND [ActiveDayRateTrend] BETWEEN -1 AND 1 AND [RecentCourseClickRate] >= 0 AND [PriorCourseClickRate] >= 0 AND [InactivityStreakDays] BETWEEN 0 AND [ObservedDays] AND [AssessmentDueRate] >= 0 AND [AssessmentOnTimeRate] BETWEEN 0 AND 1 AND [AssessmentLateOrMissingRate] BETWEEN 0 AND 1 AND [CourseProgressRatio] BETWEEN 0 AND 1 AND [CohortActivityPercentile] BETWEEN 0 AND 1) OR ([FeatureSchemaVersion] = 'withdrawal-28d-v3' AND [RecentActiveDayRate] IS NOT NULL AND [PriorActiveDayRate] IS NOT NULL AND [ActiveDayRateTrend] IS NOT NULL AND [RecentCourseClickRate] IS NOT NULL AND [PriorCourseClickRate] IS NOT NULL AND [CourseClickRateTrend] IS NOT NULL AND [InactivityStreakDays] IS NOT NULL AND [AssessmentDueRate] IS NOT NULL AND [AssessmentOnTimeRate] IS NOT NULL AND [AssessmentLateOrMissingRate] IS NOT NULL AND [CourseProgressRatio] IS NOT NULL AND [CohortActivityPercentile] IS NOT NULL AND [RecentActiveDayRate] BETWEEN 0 AND 1 AND [PriorActiveDayRate] BETWEEN 0 AND 1 AND [ActiveDayRateTrend] BETWEEN -1 AND 1 AND [RecentCourseClickRate] >= 0 AND [PriorCourseClickRate] >= 0 AND [InactivityStreakDays] BETWEEN 0 AND [ObservedDays] AND [AssessmentDueRate] >= 0 AND [AssessmentOnTimeRate] BETWEEN 0 AND 1 AND [AssessmentLateOrMissingRate] BETWEEN 0 AND 1 AND [CourseProgressRatio] BETWEEN 0 AND 1 AND [CohortActivityPercentile] BETWEEN 0 AND 1 AND [ActivityTrendAcceleration] IS NOT NULL AND [ClickVolatility] IS NOT NULL AND [ForumEngagementShare] IS NOT NULL AND [InactiveWeekRate] IS NOT NULL AND [AssessmentMissStreak] IS NOT NULL AND [ActivityTrendAcceleration] BETWEEN -2 AND 2 AND [ClickVolatility] >= 0 AND [ForumEngagementShare] BETWEEN 0 AND 1 AND [InactiveWeekRate] BETWEEN 0 AND 1 AND [AssessmentMissStreak] >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "ActivityTrendAcceleration",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "AssessmentMissStreak",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "ClickVolatility",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "ForumEngagementShare",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "InactiveWeekRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots",
                sql: "([FeatureSchemaVersion] = 'withdrawal-28d-v1' AND [ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0) OR ([FeatureSchemaVersion] = 'withdrawal-28d-v2' AND [RecentActiveDayRate] IS NOT NULL AND [PriorActiveDayRate] IS NOT NULL AND [ActiveDayRateTrend] IS NOT NULL AND [RecentCourseClickRate] IS NOT NULL AND [PriorCourseClickRate] IS NOT NULL AND [CourseClickRateTrend] IS NOT NULL AND [InactivityStreakDays] IS NOT NULL AND [AssessmentDueRate] IS NOT NULL AND [AssessmentOnTimeRate] IS NOT NULL AND [AssessmentLateOrMissingRate] IS NOT NULL AND [CourseProgressRatio] IS NOT NULL AND [CohortActivityPercentile] IS NOT NULL AND [RecentActiveDayRate] BETWEEN 0 AND 1 AND [PriorActiveDayRate] BETWEEN 0 AND 1 AND [ActiveDayRateTrend] BETWEEN -1 AND 1 AND [RecentCourseClickRate] >= 0 AND [PriorCourseClickRate] >= 0 AND [InactivityStreakDays] BETWEEN 0 AND [ObservedDays] AND [AssessmentDueRate] >= 0 AND [AssessmentOnTimeRate] BETWEEN 0 AND 1 AND [AssessmentLateOrMissingRate] BETWEEN 0 AND 1 AND [CourseProgressRatio] BETWEEN 0 AND 1 AND [CohortActivityPercentile] BETWEEN 0 AND 1)");
        }
    }
}
