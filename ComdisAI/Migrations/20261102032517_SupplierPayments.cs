using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BankId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Banks_BankId",
                        column: x => x.BankId,
                        principalTable: "Banks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SupplierPaymentId = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchaseOrderHeaderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_PurchaseOrderHeaders_PurchaseOrderHeaderId",
                        column: x => x.PurchaseOrderHeaderId,
                        principalTable: "PurchaseOrderHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_PurchaseOrderHeaderId",
                table: "SupplierPaymentAllocations",
                column: "PurchaseOrderHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierPaymentId_PurchaseOrderHeaderId",
                table: "SupplierPaymentAllocations",
                columns: new[] { "SupplierPaymentId", "PurchaseOrderHeaderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_BankId",
                table: "SupplierPayments",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierPaymentAllocations");

            migrationBuilder.DropTable(
                name: "SupplierPayments");
        }
    }
}
