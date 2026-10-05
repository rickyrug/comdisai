using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComdisAI.Migrations
{
    /// <inheritdoc />
    public partial class RoleActionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "RoleActions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "RoleActions",
                type: "TEXT",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "RoleActions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "RoleActions",
                type: "TEXT",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.Sql(
                """
                UPDATE "RoleActions"
                SET "CreatedAtUtc" = CURRENT_TIMESTAMP,
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "RoleActions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "RoleActions");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "RoleActions");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "RoleActions");
        }
    }
}
