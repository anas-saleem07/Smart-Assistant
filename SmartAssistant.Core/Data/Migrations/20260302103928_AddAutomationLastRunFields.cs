using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationLastRunFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastRunCreatedCount",
                table: "ReminderAutomationSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastRunError",
                table: "ReminderAutomationSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRunOn",
                table: "ReminderAutomationSettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRunStatus",
                table: "ReminderAutomationSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ReminderAutomationSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "LastRunCreatedCount", "LastRunError", "LastRunOn", "LastRunStatus" },
                values: new object[] { 0, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRunCreatedCount",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "LastRunError",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "LastRunOn",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "LastRunStatus",
                table: "ReminderAutomationSettings");
        }
    }
}
