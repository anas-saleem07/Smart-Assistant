using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconnectFieldsToEmailOAuthAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "EmailOAuthAccounts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReconnect",
                table: "EmailOAuthAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconnectRequiredOn",
                table: "EmailOAuthAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ReminderAutomationSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "AutoReplyKeywordsCsv",
                value: "interview,meeting,schedule,call,appointment,reschedule,rescheduled,cancel,cancelled,canceled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastError",
                table: "EmailOAuthAccounts");

            migrationBuilder.DropColumn(
                name: "NeedsReconnect",
                table: "EmailOAuthAccounts");

            migrationBuilder.DropColumn(
                name: "ReconnectRequiredOn",
                table: "EmailOAuthAccounts");

            migrationBuilder.UpdateData(
                table: "ReminderAutomationSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "AutoReplyKeywordsCsv",
                value: "interview,meeting,schedule,call,appointment");
        }
    }
}
