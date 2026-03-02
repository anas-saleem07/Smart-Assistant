using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateReminderForManualAndEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "Reminder",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceProvider",
                table: "Reminder",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Reminder",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Reminder_Type_SourceProvider_SourceId",
                table: "Reminder",
                columns: new[] { "Type", "SourceProvider", "SourceId" },
                unique: true,
                filter: "[SourceProvider] IS NOT NULL AND [SourceId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reminder_Type_SourceProvider_SourceId",
                table: "Reminder");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "Reminder");

            migrationBuilder.DropColumn(
                name: "SourceProvider",
                table: "Reminder");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Reminder");
        }
    }
}
