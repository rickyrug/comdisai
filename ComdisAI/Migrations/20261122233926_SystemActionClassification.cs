using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class SystemActionClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Action",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "Action"
                SET "IsSystem" = 1
                WHERE "NormalizedCode" IN (
                    'VIEW_HOME', 'VIEW_PRIVACY',
                    'VIEW_CUSTOMER', 'CREATE_CUSTOMER', 'EDIT_CUSTOMER', 'DELETE_CUSTOMER',
                    'VIEW_CUSTOMER_PRICE_LIST', 'CREATE_CUSTOMER_PRICE_LIST', 'EDIT_CUSTOMER_PRICE_LIST', 'DELETE_CUSTOMER_PRICE_LIST',
                    'VIEW_SUPPLIER', 'CREATE_SUPPLIER', 'EDIT_SUPPLIER', 'DELETE_SUPPLIER',
                    'VIEW_SUPPLIER_PRICE_LIST', 'CREATE_SUPPLIER_PRICE_LIST', 'EDIT_SUPPLIER_PRICE_LIST', 'DELETE_SUPPLIER_PRICE_LIST',
                    'VIEW_PRODUCT', 'CREATE_PRODUCT', 'EDIT_PRODUCT', 'DELETE_PRODUCT',
                    'VIEW_PRODUCT_CATEGORY', 'CREATE_PRODUCT_CATEGORY', 'EDIT_PRODUCT_CATEGORY', 'DELETE_PRODUCT_CATEGORY',
                    'VIEW_STATUS', 'CREATE_STATUS', 'EDIT_STATUS', 'DELETE_STATUS',
                    'VIEW_TAX', 'CREATE_TAX', 'EDIT_TAX', 'DELETE_TAX',
                    'VIEW_UOM', 'CREATE_UOM', 'EDIT_UOM', 'DELETE_UOM',
                    'VIEW_BANK', 'CREATE_BANK', 'EDIT_BANK', 'DELETE_BANK',
                    'VIEW_ACTION', 'CREATE_ACTION', 'EDIT_ACTION', 'DELETE_ACTION',
                    'VIEW_ROLE', 'CREATE_ROLE', 'EDIT_ROLE', 'DELETE_ROLE', 'MANAGE_ROLE_ACTIONS',
                    'VIEW_USER', 'CREATE_USER', 'EDIT_USER', 'DELETE_USER', 'ASSIGN_USER_ROLES', 'RESET_USER_PASSWORD',
                    'VIEW_LOGS',
                    'VIEW_SALES_ORDER', 'VIEW_SALES_REPORT', 'CREATE_SALES_ORDER', 'EDIT_SALES_ORDER',
                    'APPROVE_SALES_ORDER', 'PRINT_SALES_ORDER', 'DELIVER_SALES_ORDER', 'EDIT_DELIVERED_SALES_ORDER', 'CANCEL_SALES_ORDER',
                    'VIEW_PURCHASE_ORDER', 'VIEW_PURCHASE_REPORT', 'CREATE_PURCHASE_ORDER', 'EDIT_PURCHASE_ORDER',
                    'APPROVE_PURCHASE_ORDER', 'PRINT_PURCHASE_ORDER', 'SUPPLY_PURCHASE_ORDER', 'EDIT_SUPPLIED_PURCHASE_ORDER', 'CANCEL_PURCHASE_ORDER',
                    'VIEW_CUSTOMER_PAYMENT', 'CREATE_CUSTOMER_PAYMENT', 'EDIT_CUSTOMER_PAYMENT', 'DELETE_CUSTOMER_PAYMENT', 'ALLOCATE_CUSTOMER_PAYMENT',
                    'VIEW_SUPPLIER_PAYMENT', 'CREATE_SUPPLIER_PAYMENT', 'EDIT_SUPPLIER_PAYMENT', 'DELETE_SUPPLIER_PAYMENT', 'ALLOCATE_SUPPLIER_PAYMENT'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Action");
        }
    }
}
