using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderCalendarFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CalendarCreatedOn",
                table: "EmailProcessed",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalendarEventId",
                table: "EmailProcessed",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalendarLastError",
                table: "EmailProcessed",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarCreatedOn",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "CalendarEventId",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "CalendarLastError",
                table: "EmailProcessed");
        }
    }
}
