using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF 產生的 migration 索引欄位陣列

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_OpeningStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inv_opening_stock",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    request_key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    opening_date = table.Column<DateOnly>(type: "date", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    posted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_opening_stock", x => x.id);
                    table.UniqueConstraint("ak_inv_opening_stock_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_opening_stock_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "inv_opening_stock_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    opening_stock_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_opening_stock_line", x => x.id);
                    table.CheckConstraint("ck_inv_opening_stock_line_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_inv_opening_stock_line_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_opening_stock_line_unit_cost", "[unit_cost] >= 0 AND [amount] >= 0");
                    table.ForeignKey(
                        name: "fk_inv_opening_stock_line_inv_opening_stock_company_id_opening_stock_id",
                        columns: x => new { x.company_id, x.opening_stock_id },
                        principalTable: "inv_opening_stock",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_opening_stock_company_id_opening_date_id",
                table: "inv_opening_stock",
                columns: new[] { "company_id", "opening_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_opening_stock_company_id_request_key",
                table: "inv_opening_stock",
                columns: new[] { "company_id", "request_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_opening_stock_line_company_id_opening_stock_id_line_no",
                table: "inv_opening_stock_line",
                columns: new[] { "company_id", "opening_stock_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_opening_stock_line_company_id_warehouse_id_location_id",
                table: "inv_opening_stock_line",
                columns: new[] { "company_id", "warehouse_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ux_inv_opening_stock_line_balance_key",
                table: "inv_opening_stock_line",
                columns: new[] { "company_id", "item_id", "warehouse_id", "location_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inv_opening_stock_line");

            migrationBuilder.DropTable(
                name: "inv_opening_stock");
        }
    }
}
