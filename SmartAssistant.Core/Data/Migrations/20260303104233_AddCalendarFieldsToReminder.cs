using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarFieldsToReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarEventId",
                table: "Reminder",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalendarSyncError",
                table: "Reminder",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CalendarSyncedOn",
                table: "Reminder",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarEventId",
                table: "Reminder");

            migrationBuilder.DropColumn(
                name: "CalendarSyncError",
                table: "Reminder");

            migrationBuilder.DropColumn(
                name: "CalendarSyncedOn",
                table: "Reminder");
        }
    }
}
