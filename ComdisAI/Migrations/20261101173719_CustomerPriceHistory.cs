using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class CustomerPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerPriceHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    NewPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPriceHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPriceHistory_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPriceHistory_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPriceHistory_CustomerId_ProductId_CreatedAtUtc_Id",
                table: "CustomerPriceHistory",
                columns: new[] { "CustomerId", "ProductId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPriceHistory_ProductId",
                table: "CustomerPriceHistory",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPriceHistory");
        }
    }
}
