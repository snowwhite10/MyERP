using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF 產生的 migration 索引欄位陣列

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_StockLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_inv_reference_company_id_id",
                table: "inv_reference",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "inv_stock_movement",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_line_no = table.Column<int>(type: "int", nullable: false),
                    direction = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    reference_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("pk_inv_stock_movement", x => x.id);
                    table.CheckConstraint("ck_inv_stock_movement_direction", "[direction] IN ('In', 'Out')");
                    table.CheckConstraint("ck_inv_stock_movement_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_stock_movement_source", "[source_id] > 0 AND [source_line_no] > 0");
                    table.CheckConstraint("ck_inv_stock_movement_unit_cost", "[unit_cost] IS NULL OR ([unit_cost] >= 0 AND [direction] = 'In')");
                    table.ForeignKey(
                        name: "fk_inv_stock_movement_inv_reference_company_id_reference_id",
                        columns: x => new { x.company_id, x.reference_id },
                        principalTable: "inv_reference",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_balance",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "item_id", "warehouse_id", "location_id" })
                .Annotation("SqlServer:Include", new[] { "direction", "quantity" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_company_id_business_date",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_company_id_reference_id",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_company_id_source_type_source_id_source_line_no_direction",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "source_type", "source_id", "source_line_no", "direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_company_id_warehouse_id_location_id",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "warehouse_id", "location_id" });

            // 異動帳只增不改（MASTER_PROMPT 6.7、ADR-053）：任何 UPDATE／DELETE 一律拒絕，更正以反向單據追加
            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER trg_inv_stock_movement_immutable ON inv_stock_movement
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51001, 'inv_stock_movement is append-only; post a reversing source instead.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_inv_stock_movement_immutable;");

            migrationBuilder.DropTable(
                name: "inv_stock_movement");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_inv_reference_company_id_id",
                table: "inv_reference");
        }
    }
}
