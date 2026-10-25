using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDefaultTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultTaxId",
                table: "Customers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_DefaultTaxId",
                table: "Customers",
                column: "DefaultTaxId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Taxes_DefaultTaxId",
                table: "Customers",
                column: "DefaultTaxId",
                principalTable: "Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Taxes_DefaultTaxId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_DefaultTaxId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DefaultTaxId",
                table: "Customers");
        }
    }
}
