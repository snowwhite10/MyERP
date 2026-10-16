using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core generates literal arrays for composite indexes.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_SafetyStockThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inv_safety_stock_threshold",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    minimum_quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_safety_stock_threshold", x => x.id);
                    table.CheckConstraint("ck_inv_safety_stock_threshold_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL AND [deleted_by] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [deleted_by] IS NOT NULL)");
                    table.CheckConstraint("ck_inv_safety_stock_threshold_quantity", "[minimum_quantity] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_safety_stock_threshold_company_id_item_id_warehouse_id",
                table: "inv_safety_stock_threshold",
                columns: new[] { "company_id", "item_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_safety_stock_threshold_company_id_warehouse_id_item_id_id",
                table: "inv_safety_stock_threshold",
                columns: new[] { "company_id", "warehouse_id", "item_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inv_safety_stock_threshold");
        }
    }
}
