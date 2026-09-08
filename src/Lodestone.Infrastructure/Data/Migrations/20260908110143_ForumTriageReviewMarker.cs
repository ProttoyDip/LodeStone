using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lodestone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ForumTriageReviewMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastModeratorReviewAtUtc",
                table: "ForumPosts",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastModeratorReviewAtUtc",
                table: "ForumPosts");
        }
    }
}
