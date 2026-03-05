using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CalendarEnabled",
                table: "ReminderAutomationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CalendarId",
                table: "ReminderAutomationSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ReminderAutomationSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CalendarEnabled", "CalendarId" },
                values: new object[] { true, "primary" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarEnabled",
                table: "ReminderAutomationSettings");

            migrationBuilder.DropColumn(
                name: "CalendarId",
                table: "ReminderAutomationSettings");
        }
    }
}
