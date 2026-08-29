using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodestone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsentGatedRiskMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Privacy boundary: rows created before explicit monitoring consent cannot be
            // retained or safely upgraded to the versioned snapshot/model contract.
            // Queue rows must be removed before their scores because both FKs are RESTRICT.
            migrationBuilder.Sql(
                """
                DELETE FROM [RiskQueueEntries];
                DELETE FROM [RiskScores];
                DELETE FROM [ActivityLogs];

                UPDATE [StudentProfiles]
                SET [StudentNumber] = UPPER(LTRIM(RTRIM([StudentNumber])))
                WHERE [StudentNumber] IS NOT NULL;

                UPDATE [StudentProfiles]
                SET [StudentNumber] = NULL
                WHERE [StudentNumber] IS NOT NULL
                  AND (
                      LEN([StudentNumber]) NOT BETWEEN 1 AND 64
                      OR LEFT([StudentNumber], 1) COLLATE Latin1_General_100_BIN2 NOT LIKE '[A-Z0-9]'
                      OR [StudentNumber] COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9._/-]%'
                  );
                """);

            migrationBuilder.DropIndex(
                name: "IX_RiskScores_StudentProfileId",
                table: "RiskScores");

            migrationBuilder.DropIndex(
                name: "IX_RiskQueueEntries_StudentProfileId",
                table: "RiskQueueEntries");

            migrationBuilder.AlterColumn<string>(
                name: "StudentNumber",
                table: "StudentProfiles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Program",
                table: "StudentProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ModelVersion",
                table: "RiskScores",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourseKey",
                table: "RiskScores",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeatureSchemaVersion",
                table: "RiskScores",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RiskFeatureSnapshotId",
                table: "RiskScores",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RiskScoringRunId",
                table: "RiskScores",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WindowEndUtc",
                table: "RiskScores",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<string>(
                name: "ResolvedByUserId",
                table: "RiskQueueEntries",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSignaledAtUtc",
                table: "RiskQueueEntries",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RiskQueueEntries",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "TriggerRiskScoreId",
                table: "RiskQueueEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RiskFeatureSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    CourseKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ObservedDays = table.Column<int>(type: "int", nullable: false),
                    FeatureSchemaVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SourceFileSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActiveDayRate = table.Column<float>(type: "real", nullable: false),
                    ActivitySpanDays = table.Column<float>(type: "real", nullable: false),
                    DaysSinceLastAccess = table.Column<float>(type: "real", nullable: false),
                    ForumInteractionCount = table.Column<float>(type: "real", nullable: false),
                    CourseInteractionCount = table.Column<float>(type: "real", nullable: false),
                    LateOrMissingAssignmentCount = table.Column<float>(type: "real", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskFeatureSnapshots", x => x.Id);
                    table.CheckConstraint("CK_RiskFeatureSnapshots_Features", "[ActiveDayRate] >= 0 AND [ActiveDayRate] <= 1 AND [ActivitySpanDays] >= 0 AND [ActivitySpanDays] <= [ObservedDays] AND [DaysSinceLastAccess] >= 0 AND [DaysSinceLastAccess] <= [ObservedDays] AND [ForumInteractionCount] >= 0 AND [CourseInteractionCount] >= 0 AND [LateOrMissingAssignmentCount] >= 0");
                    table.CheckConstraint("CK_RiskFeatureSnapshots_ObservedDays", "[ObservedDays] > 0 AND [ObservedDays] <= 365");
                    table.ForeignKey(
                        name: "FK_RiskFeatureSnapshots_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskMonitoringConsentHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    IsConsented = table.Column<bool>(type: "bit", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskMonitoringConsentHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskMonitoringConsentHistory_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskMonitoringConsents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    IsConsented = table.Column<bool>(type: "bit", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConsentedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskMonitoringConsents", x => x.Id);
                    table.CheckConstraint("CK_RiskMonitoringConsents_State", "[IsConsented] = 0 OR ([ConsentedAtUtc] IS NOT NULL AND [WithdrawnAtUtc] IS NULL)");
                    table.ForeignKey(
                        name: "FK_RiskMonitoringConsents_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskScoringRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FeatureSchemaVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CandidateCount = table.Column<int>(type: "int", nullable: false),
                    ScoredCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    QueueCreatedCount = table.Column<int>(type: "int", nullable: false),
                    QueueEscalatedCount = table.Column<int>(type: "int", nullable: false),
                    FailureSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskScoringRuns", x => x.Id);
                    table.CheckConstraint("CK_RiskScoringRuns_Counts", "[CandidateCount] >= 0 AND [ScoredCount] >= 0 AND [SkippedCount] >= 0 AND [FailedCount] >= 0 AND [QueueCreatedCount] >= 0 AND [QueueEscalatedCount] >= 0");
                    table.CheckConstraint("CK_RiskScoringRuns_Status", "[Status] >= 0 AND [Status] <= 4");
                });

            migrationBuilder.CreateTable(
                name: "StudentNumberClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    ClaimedStudentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNumberClaims", x => x.Id);
                    table.CheckConstraint("CK_StudentNumberClaims_Review", "([Status] = 0 AND [ReviewedAtUtc] IS NULL AND [ReviewedByUserId] IS NULL) OR ([Status] IN (1, 2) AND [ReviewedAtUtc] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_StudentNumberClaims_Status", "[Status] >= 0 AND [Status] <= 2");
                    table.ForeignKey(
                        name: "FK_StudentNumberClaims_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Existing profile identifiers are treated as untrusted submissions, never as
            // verified mappings. Administrators must review these pending claims explicitly.
            migrationBuilder.Sql(
                """
                INSERT INTO [StudentNumberClaims]
                    ([StudentProfileId], [ClaimedStudentNumber], [Status], [SubmittedAtUtc],
                     [ReviewedAtUtc], [ReviewedByUserId], [CreatedAtUtc], [CreatedBy],
                     [ModifiedAtUtc], [ModifiedBy])
                SELECT [Id], [StudentNumber], 0, SYSUTCDATETIME(),
                       NULL, NULL, SYSUTCDATETIME(), [UserId], NULL, NULL
                FROM [StudentProfiles]
                WHERE [StudentNumber] IS NOT NULL;

                UPDATE [StudentProfiles]
                SET [StudentNumber] = NULL
                WHERE [StudentNumber] IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_StudentProfiles_StudentNumber",
                table: "StudentProfiles",
                column: "StudentNumber",
                unique: true,
                filter: "[StudentNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RiskScores_RiskScoringRunId",
                table: "RiskScores",
                column: "RiskScoringRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskScores_StudentProfileId_ScoredAtUtc",
                table: "RiskScores",
                columns: new[] { "StudentProfileId", "ScoredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RiskScores_Snapshot_Model",
                table: "RiskScores",
                columns: new[] { "RiskFeatureSnapshotId", "ModelVersion" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskScores_Level",
                table: "RiskScores",
                sql: "[Level] >= 0 AND [Level] <= 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskScores_Probability",
                table: "RiskScores",
                sql: "[Probability] >= 0 AND [Probability] <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_RiskQueueEntries_IsResolved_Level_LastSignaledAtUtc",
                table: "RiskQueueEntries",
                columns: new[] { "IsResolved", "Level", "LastSignaledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskQueueEntries_TriggerRiskScoreId",
                table: "RiskQueueEntries",
                column: "TriggerRiskScoreId");

            migrationBuilder.CreateIndex(
                name: "UX_RiskQueueEntries_OneOpenPerStudent",
                table: "RiskQueueEntries",
                column: "StudentProfileId",
                unique: true,
                filter: "[IsResolved] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskQueueEntries_Level",
                table: "RiskQueueEntries",
                sql: "[Level] >= 0 AND [Level] <= 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RiskQueueEntries_Resolution",
                table: "RiskQueueEntries",
                sql: "([IsResolved] = 0 AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([IsResolved] = 1 AND [ResolvedAtUtc] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ActivityLogs_NonNegativeCounts",
                table: "ActivityLogs",
                sql: "[LoginCount] >= 0 AND [ForumInteractions] >= 0 AND [CourseInteractions] >= 0 AND [DaysSinceLastAccess] >= 0 AND [AssignmentsLateCount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_RiskFeatureSnapshots_FeatureSchemaVersion_WindowEndUtc",
                table: "RiskFeatureSnapshots",
                columns: new[] { "FeatureSchemaVersion", "WindowEndUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RiskFeatureSnapshots_Student_Course_Window_Schema",
                table: "RiskFeatureSnapshots",
                columns: new[] { "StudentProfileId", "CourseKey", "WindowEndUtc", "FeatureSchemaVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskMonitoringConsentHistory_StudentProfileId_ChangedAtUtc",
                table: "RiskMonitoringConsentHistory",
                columns: new[] { "StudentProfileId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskMonitoringConsents_StudentProfileId",
                table: "RiskMonitoringConsents",
                column: "StudentProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskScoringRuns_RunKey",
                table: "RiskScoringRuns",
                column: "RunKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskScoringRuns_StartedAtUtc",
                table: "RiskScoringRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNumberClaims_ClaimedStudentNumber_Status",
                table: "StudentNumberClaims",
                columns: new[] { "ClaimedStudentNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentNumberClaims_Status_SubmittedAtUtc",
                table: "StudentNumberClaims",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_StudentNumberClaims_OnePendingPerStudent",
                table: "StudentNumberClaims",
                column: "StudentProfileId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_RiskQueueEntries_RiskScores_TriggerRiskScoreId",
                table: "RiskQueueEntries",
                column: "TriggerRiskScoreId",
                principalTable: "RiskScores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RiskScores_RiskFeatureSnapshots_RiskFeatureSnapshotId",
                table: "RiskScores",
                column: "RiskFeatureSnapshotId",
                principalTable: "RiskFeatureSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RiskScores_RiskScoringRuns_RiskScoringRunId",
                table: "RiskScores",
                column: "RiskScoringRunId",
                principalTable: "RiskScoringRuns",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The legacy monitoring purge and conversion of former profile identifiers into
            // pending claims are intentionally irreversible; Down cannot reconstruct that data.
            migrationBuilder.DropForeignKey(
                name: "FK_RiskQueueEntries_RiskScores_TriggerRiskScoreId",
                table: "RiskQueueEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_RiskScores_RiskFeatureSnapshots_RiskFeatureSnapshotId",
                table: "RiskScores");

            migrationBuilder.DropForeignKey(
                name: "FK_RiskScores_RiskScoringRuns_RiskScoringRunId",
                table: "RiskScores");

            migrationBuilder.DropTable(
                name: "RiskFeatureSnapshots");

            migrationBuilder.DropTable(
                name: "RiskMonitoringConsentHistory");

            migrationBuilder.DropTable(
                name: "RiskMonitoringConsents");

            migrationBuilder.DropTable(
                name: "RiskScoringRuns");

            migrationBuilder.DropTable(
                name: "StudentNumberClaims");

            migrationBuilder.DropIndex(
                name: "UX_StudentProfiles_StudentNumber",
                table: "StudentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_RiskScores_RiskScoringRunId",
                table: "RiskScores");

            migrationBuilder.DropIndex(
                name: "IX_RiskScores_StudentProfileId_ScoredAtUtc",
                table: "RiskScores");

            migrationBuilder.DropIndex(
                name: "UX_RiskScores_Snapshot_Model",
                table: "RiskScores");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskScores_Level",
                table: "RiskScores");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskScores_Probability",
                table: "RiskScores");

            migrationBuilder.DropIndex(
                name: "IX_RiskQueueEntries_IsResolved_Level_LastSignaledAtUtc",
                table: "RiskQueueEntries");

            migrationBuilder.DropIndex(
                name: "IX_RiskQueueEntries_TriggerRiskScoreId",
                table: "RiskQueueEntries");

            migrationBuilder.DropIndex(
                name: "UX_RiskQueueEntries_OneOpenPerStudent",
                table: "RiskQueueEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskQueueEntries_Level",
                table: "RiskQueueEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RiskQueueEntries_Resolution",
                table: "RiskQueueEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ActivityLogs_NonNegativeCounts",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "CourseKey",
                table: "RiskScores");

            migrationBuilder.DropColumn(
                name: "FeatureSchemaVersion",
                table: "RiskScores");

            migrationBuilder.DropColumn(
                name: "RiskFeatureSnapshotId",
                table: "RiskScores");

            migrationBuilder.DropColumn(
                name: "RiskScoringRunId",
                table: "RiskScores");

            migrationBuilder.DropColumn(
                name: "WindowEndUtc",
                table: "RiskScores");

            migrationBuilder.DropColumn(
                name: "LastSignaledAtUtc",
                table: "RiskQueueEntries");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RiskQueueEntries");

            migrationBuilder.DropColumn(
                name: "TriggerRiskScoreId",
                table: "RiskQueueEntries");

            migrationBuilder.AlterColumn<string>(
                name: "StudentNumber",
                table: "StudentProfiles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Program",
                table: "StudentProfiles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ModelVersion",
                table: "RiskScores",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ResolvedByUserId",
                table: "RiskQueueEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskScores_StudentProfileId",
                table: "RiskScores",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskQueueEntries_StudentProfileId",
                table: "RiskQueueEntries",
                column: "StudentProfileId");
        }
    }
}
