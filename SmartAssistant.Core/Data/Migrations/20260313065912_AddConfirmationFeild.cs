using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAssistant.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationFeild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WaitingForExternalConfirmation",
                table: "EmailProcessed",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaitingForExternalConfirmation",
                table: "EmailProcessed");
        }
    }
}
