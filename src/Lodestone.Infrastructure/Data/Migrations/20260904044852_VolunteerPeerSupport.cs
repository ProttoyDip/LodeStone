using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodestone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class VolunteerPeerSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "VolunteerProfiles",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Availability",
                table: "VolunteerProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "VolunteerProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            // VolunteerProfile.IsActive is declared as true; scaffolding defaults a bool column to
            // the CLR default instead, which would land any row inserted outside EF as inactive.
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "VolunteerProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Skills",
                table: "VolunteerProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    VolunteerProfileId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Availability = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsVisibleToVolunteers = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportRequests_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportRequests_VolunteerProfiles_VolunteerProfileId",
                        column: x => x.VolunteerProfileId,
                        principalTable: "VolunteerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VolunteerAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VolunteerProfileId = table.Column<int>(type: "int", nullable: false),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VolunteerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VolunteerAssignments_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VolunteerAssignments_VolunteerProfiles_VolunteerProfileId",
                        column: x => x.VolunteerProfileId,
                        principalTable: "VolunteerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportInteractions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupportRequestId = table.Column<int>(type: "int", nullable: false),
                    VolunteerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    StudentUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    EscalatedToCounselor = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportInteractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportInteractions_SupportRequests_SupportRequestId",
                        column: x => x.SupportRequestId,
                        principalTable: "SupportRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerProfiles_IsApproved_IsActive",
                table: "VolunteerProfiles",
                columns: new[] { "IsApproved", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportInteractions_SupportRequestId_CreatedAtUtc",
                table: "SupportInteractions",
                columns: new[] { "SupportRequestId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportInteractions_VolunteerUserId_Type",
                table: "SupportInteractions",
                columns: new[] { "VolunteerUserId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_Status_IsVisibleToVolunteers_CreatedAtUtc",
                table: "SupportRequests",
                columns: new[] { "Status", "IsVisibleToVolunteers", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_StudentProfileId_CreatedAtUtc",
                table: "SupportRequests",
                columns: new[] { "StudentProfileId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_VolunteerProfileId_Status_CreatedAtUtc",
                table: "SupportRequests",
                columns: new[] { "VolunteerProfileId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerAssignments_StudentProfileId_IsActive",
                table: "VolunteerAssignments",
                columns: new[] { "StudentProfileId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerAssignments_VolunteerProfileId_IsActive",
                table: "VolunteerAssignments",
                columns: new[] { "VolunteerProfileId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerAssignments_VolunteerProfileId_StudentProfileId",
                table: "VolunteerAssignments",
                columns: new[] { "VolunteerProfileId", "StudentProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportInteractions");

            migrationBuilder.DropTable(
                name: "VolunteerAssignments");

            migrationBuilder.DropTable(
                name: "SupportRequests");

            migrationBuilder.DropIndex(
                name: "IX_VolunteerProfiles_IsApproved_IsActive",
                table: "VolunteerProfiles");

            migrationBuilder.DropColumn(
                name: "Availability",
                table: "VolunteerProfiles");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "VolunteerProfiles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "VolunteerProfiles");

            migrationBuilder.DropColumn(
                name: "Skills",
                table: "VolunteerProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "VolunteerProfiles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
