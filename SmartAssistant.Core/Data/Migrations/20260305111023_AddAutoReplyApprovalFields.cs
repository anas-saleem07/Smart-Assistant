using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoReplyApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAutoReplyAfterOfficeHours",
                table: "ReminderAutomationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireApprovalAfterOfficeHours",
                table: "ReminderAutomationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProposedEndUtc",
                table: "EmailProcessed",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProposedStartUtc",
                table: "EmailProcessed",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyDraftBody",
                table: "EmailProcessed",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReplyRequiresApproval",
                table: "EmailProcessed",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "ReminderAutomationSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AllowAutoReplyAfterOfficeHours", "RequireApprovalAfterOfficeHours" },
                values: new object[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowAutoReplyAfterOfficeHours",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "RequireApprovalAfterOfficeHours",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "ProposedEndUtc",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "ProposedStartUtc",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "ReplyDraftBody",
                table: "EmailProcessed");

            migrationBuilder.DropColumn(
                name: "ReplyRequiresApproval",
                table: "EmailProcessed");
        }
    }
}
