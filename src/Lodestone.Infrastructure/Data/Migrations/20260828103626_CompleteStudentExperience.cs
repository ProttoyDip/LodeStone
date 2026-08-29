using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodestone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteStudentExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CounselorBookings_CounselorProfileId",
                table: "CounselorBookings");

            migrationBuilder.DropIndex(
                name: "IX_CounselorBookings_StudentProfileId",
                table: "CounselorBookings");

            migrationBuilder.DropIndex(
                name: "IX_CounselorAvailabilitySlots_CounselorProfileId",
                table: "CounselorAvailabilitySlots");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogs_StudentProfileId",
                table: "ActivityLogs");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CounselorAvailabilitySlots",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_CounselorBookings_CounselorProfileId_ScheduledForUtc",
                table: "CounselorBookings",
                columns: new[] { "CounselorProfileId", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CounselorBookings_StudentProfileId_ScheduledForUtc",
                table: "CounselorBookings",
                columns: new[] { "StudentProfileId", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CounselorAvailabilitySlots_CounselorProfileId_StartUtc",
                table: "CounselorAvailabilitySlots",
                columns: new[] { "CounselorProfileId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_StudentProfileId_OccurredAtUtc",
                table: "ActivityLogs",
                columns: new[] { "StudentProfileId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CounselorBookings_CounselorProfileId_ScheduledForUtc",
                table: "CounselorBookings");

            migrationBuilder.DropIndex(
                name: "IX_CounselorBookings_StudentProfileId_ScheduledForUtc",
                table: "CounselorBookings");

            migrationBuilder.DropIndex(
                name: "IX_CounselorAvailabilitySlots_CounselorProfileId_StartUtc",
                table: "CounselorAvailabilitySlots");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogs_StudentProfileId_OccurredAtUtc",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CounselorAvailabilitySlots");

            migrationBuilder.CreateIndex(
                name: "IX_CounselorBookings_CounselorProfileId",
                table: "CounselorBookings",
                column: "CounselorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CounselorBookings_StudentProfileId",
                table: "CounselorBookings",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CounselorAvailabilitySlots_CounselorProfileId",
                table: "CounselorAvailabilitySlots",
                column: "CounselorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_StudentProfileId",
                table: "ActivityLogs",
                column: "StudentProfileId");
        }
    }
}
