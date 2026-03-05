using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoReplyQueueAndQuotaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiCallsToday",
                table: "ReminderAutomationSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiDailyLimit",
                table: "ReminderAutomationSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AiLastError",
                table: "ReminderAutomationSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiPausedUntilUtc",
                table: "ReminderAutomationSettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiUsageDayUtc",
                table: "ReminderAutomationSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoReplyEnabled",
                table: "ReminderAutomationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AutoReplyKeywordsCsv",
                table: "ReminderAutomationSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OfficeEndHour",
                table: "ReminderAutomationSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OfficeStartHour",
                table: "ReminderAutomationSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SlotMinutes",
                table: "ReminderAutomationSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TimezoneId",
                table: "ReminderAutomationSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Replied",
                table: "EmailProcessed",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RepliedOn",
                table: "EmailProcessed",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyLastError",
                table: "EmailProcessed",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReplyNeeded",
                table: "EmailProcessed",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReplyQueuedOn",
                table: "EmailProcessed",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ReminderAutomationSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AiCallsToday", "AiDailyLimit", "AiLastError", "AiPausedUntilUtc", "AiUsageDayUtc", "AutoReplyEnabled", "AutoReplyKeywordsCsv", "OfficeEndHour", "OfficeStartHour", "SlotMinutes", "TimezoneId" },
                values: new object[] { 0, 50, null, null, null, false, "interview,meeting,schedule,call,appointment", 18, 9, 30, "Asia/Karachi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiCallsToday",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "AiDailyLimit",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "AiLastError",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "AiPausedUntilUtc",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "AiUsageDayUtc",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "AutoReplyEnabled",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "AutoReplyKeywordsCsv",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "OfficeEndHour",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "OfficeStartHour",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "SlotMinutes",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "TimezoneId",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "Replied",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "RepliedOn",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "ReplyLastError",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "ReplyNeeded",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "ReplyQueuedOn",
                table: "EmailProcessed");
        }
    }
}
