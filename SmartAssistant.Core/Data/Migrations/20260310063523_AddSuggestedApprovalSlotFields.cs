using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestedApprovalSlotFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestedCalendarEventId",
                table: "EmailProcessed",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedCalendarHtmlLink",
                table: "EmailProcessed",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuggestedEndUtc",
                table: "EmailProcessed",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuggestedStartUtc",
                table: "EmailProcessed",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestedCalendarEventId",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "SuggestedCalendarHtmlLink",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "SuggestedEndUtc",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "SuggestedStartUtc",
                table: "EmailProcessed");
        }
    }
}
