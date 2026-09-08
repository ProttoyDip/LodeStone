using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodestone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PeerChatReadMarkersVolunteerAwayNudgeBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AwayMessage",
                table: "VolunteerProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AwayUntilUtc",
                table: "VolunteerProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StudentLastReadAtUtc",
                table: "SupportRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VolunteerLastReadAtUtc",
                table: "SupportRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CounselorBookingId",
                table: "Nudges",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayMessage",
                table: "VolunteerProfiles");

            migrationBuilder.DropColumn(
                name: "AwayUntilUtc",
                table: "VolunteerProfiles");

            migrationBuilder.DropColumn(
                name: "StudentLastReadAtUtc",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "VolunteerLastReadAtUtc",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "CounselorBookingId",
                table: "Nudges");
        }
    }
}
