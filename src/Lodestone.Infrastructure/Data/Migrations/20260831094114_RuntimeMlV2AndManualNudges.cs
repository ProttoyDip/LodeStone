using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodestone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeMlV2AndManualNudges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Nudges_StudentProfileId",
                table: "Nudges");

            migrationBuilder.AddColumn<float>(
                name: "ActiveDayRateTrend",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "AssessmentDueRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "AssessmentLateOrMissingRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "AssessmentOnTimeRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "CohortActivityPercentile",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "CourseClickRateTrend",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "CourseProgressRatio",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "InactivityStreakDays",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "PriorActiveDayRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "PriorCourseClickRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "RecentActiveDayRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "RecentCourseClickRate",
                table: "RiskFeatureSnapshots",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAtUtc",
                table: "Nudges",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableAtUtc",
                table: "Nudges",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "DismissedAtUtc",
                table: "Nudges",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "Nudges",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "DATEADD(day, 14, SYSUTCDATETIME())");

            migrationBuilder.AddColumn<bool>(
                name: "IsManualCounselorNudge",
                table: "Nudges",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SnoozedUntilUtc",
                table: "Nudges",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoteProtectionVersion",
                table: "MoodJournalEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "StudentNudgePreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    IsInAppNudgesEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNudgePreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentNudgePreferences_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots",
                sql: "([FeatureSchemaVersion] = 'withdrawal-28d-v1' AND [ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0) OR ([FeatureSchemaVersion] = 'withdrawal-28d-v2' AND [RecentActiveDayRate] IS NOT NULL AND [PriorActiveDayRate] IS NOT NULL AND [ActiveDayRateTrend] IS NOT NULL AND [RecentCourseClickRate] IS NOT NULL AND [PriorCourseClickRate] IS NOT NULL AND [CourseClickRateTrend] IS NOT NULL AND [InactivityStreakDays] IS NOT NULL AND [AssessmentDueRate] IS NOT NULL AND [AssessmentOnTimeRate] IS NOT NULL AND [AssessmentLateOrMissingRate] IS NOT NULL AND [CourseProgressRatio] IS NOT NULL AND [CohortActivityPercentile] IS NOT NULL AND [RecentActiveDayRate] BETWEEN 0 AND 1 AND [PriorActiveDayRate] BETWEEN 0 AND 1 AND [ActiveDayRateTrend] BETWEEN -1 AND 1 AND [RecentCourseClickRate] >= 0 AND [PriorCourseClickRate] >= 0 AND [InactivityStreakDays] BETWEEN 0 AND [ObservedDays] AND [AssessmentDueRate] >= 0 AND [AssessmentOnTimeRate] BETWEEN 0 AND 1 AND [AssessmentLateOrMissingRate] BETWEEN 0 AND 1 AND [CourseProgressRatio] BETWEEN 0 AND 1 AND [CohortActivityPercentile] BETWEEN 0 AND 1)");

            migrationBuilder.CreateIndex(
                name: "IX_Nudges_StudentProfileId_Status_AvailableAtUtc",
                table: "Nudges",
                columns: new[] { "StudentProfileId", "Status", "AvailableAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Nudges_VisibilityWindow",
                table: "Nudges",
                sql: "[ExpiresAtUtc] > [AvailableAtUtc]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MoodJournalEntries_NoteProtectionVersion",
                table: "MoodJournalEntries",
                sql: "[NoteProtectionVersion] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNudgePreferences_StudentProfileId",
                table: "StudentNudgePreferences",
                column: "StudentProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentNudgePreferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Nudges_StudentProfileId_Status_AvailableAtUtc",
                table: "Nudges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Nudges_VisibilityWindow",
                table: "Nudges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MoodJournalEntries_NoteProtectionVersion",
                table: "MoodJournalEntries");

            migrationBuilder.DropColumn(
                name: "ActiveDayRateTrend",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "AssessmentDueRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "AssessmentLateOrMissingRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "AssessmentOnTimeRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "CohortActivityPercentile",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "CourseClickRateTrend",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "CourseProgressRatio",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "InactivityStreakDays",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "PriorActiveDayRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "PriorCourseClickRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "RecentActiveDayRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "RecentCourseClickRate",
                table: "RiskFeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAtUtc",
                table: "Nudges");

            migrationBuilder.DropColumn(
                name: "AvailableAtUtc",
                table: "Nudges");

            migrationBuilder.DropColumn(
                name: "DismissedAtUtc",
                table: "Nudges");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "Nudges");

            migrationBuilder.DropColumn(
                name: "IsManualCounselorNudge",
                table: "Nudges");

            migrationBuilder.DropColumn(
                name: "SnoozedUntilUtc",
                table: "Nudges");

            migrationBuilder.DropColumn(
                name: "NoteProtectionVersion",
                table: "MoodJournalEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskFeatureSnapshots_Features",
                table: "RiskFeatureSnapshots",
                sql: "[ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Nudges_StudentProfileId",
                table: "Nudges",
                column: "StudentProfileId");
        }
    }
}
