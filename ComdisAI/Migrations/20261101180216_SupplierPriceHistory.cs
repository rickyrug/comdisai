using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierPriceHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_SupplierPriceHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPriceHistory_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPriceHistory_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPriceHistory_ProductId",
                table: "SupplierPriceHistory",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPriceHistory_SupplierId_ProductId_CreatedAtUtc_Id",
                table: "SupplierPriceHistory",
                columns: new[] { "SupplierId", "ProductId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierPriceHistory");
        }
    }
}
