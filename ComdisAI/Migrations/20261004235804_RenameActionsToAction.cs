using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class RenameActionsToAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Actions",
                newName: "Action");

            migrationBuilder.DropIndex(
                name: "IX_Actions_NormalizedCode",
                table: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_Action_NormalizedCode",
                table: "Action",
                column: "NormalizedCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Action",
                newName: "Actions");

            migrationBuilder.DropIndex(
                name: "IX_Action_NormalizedCode",
                table: "Actions");

            migrationBuilder.CreateIndex(
                name: "IX_Actions_NormalizedCode",
                table: "Actions",
                column: "NormalizedCode",
                unique: true);
        }
    }
}
