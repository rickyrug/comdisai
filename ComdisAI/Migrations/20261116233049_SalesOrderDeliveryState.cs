using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class SalesOrderDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDelivered",
                table: "SalesOrderHeaders",
                type: "INTEGER",
                nullable: true,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE SalesOrderHeaders SET IsDelivered = 1
                WHERE StatusId IN (
                    SELECT Id FROM Statuses
                    WHERE NormalizedEntity = 'SALESORDER' AND NormalizedName = 'DELIVERED'
                )
                """);
            migrationBuilder.Sql("""
                UPDATE SalesOrderHeaders SET IsDelivered = NULL
                WHERE StatusId IN (
                    SELECT Id FROM Statuses
                    WHERE NormalizedEntity = 'SALESORDER' AND NormalizedName = 'PAID'
                )
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDelivered",
                table: "SalesOrderHeaders");
        }
    }
}
