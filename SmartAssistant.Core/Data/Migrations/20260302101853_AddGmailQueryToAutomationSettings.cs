using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailQueryToAutomationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GmailQuery",
                table: "ReminderAutomationSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "ReminderAutomationSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "GmailQuery",
                value: "in:inbox newer_than:7d -category:promotions -category:social");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GmailQuery",
                table: "ReminderAutomationSettings");
        }
    }
}
