using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class ProductCategoryOnProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductCategory",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Products_ProductCategory",
                table: "Products",
                column: "ProductCategory");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductCategories_ProductCategory",
                table: "Products",
                column: "ProductCategory",
                principalTable: "ProductCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductCategories_ProductCategory",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ProductCategory",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ProductCategory",
                table: "Products");
        }
    }
}
