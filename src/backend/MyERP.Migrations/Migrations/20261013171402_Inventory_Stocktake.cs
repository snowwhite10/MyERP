using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF 產生的 migration 索引欄位陣列

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_Stocktake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inv_stocktake",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    scope_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    snapshot_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    snapshot_movement_id = table.Column<long>(type: "bigint", nullable: true),
                    remark = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    posted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_stocktake", x => x.id);
                    table.UniqueConstraint("ak_inv_stocktake_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_stocktake_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_inv_stocktake_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_inv_stocktake_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
                    table.CheckConstraint("ck_inv_stocktake_scope_json", "ISJSON([scope_json]) = 1");
                    table.CheckConstraint("ck_inv_stocktake_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Posted')");
                });

            migrationBuilder.CreateTable(
                name: "inv_stocktake_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    stocktake_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    tracking_mode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    book_quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    counted_quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    is_added = table.Column<bool>(type: "bit", nullable: false),
                    recount_required = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_stocktake_line", x => x.id);
                    table.UniqueConstraint("ak_inv_stocktake_line_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_stocktake_line_added", "[is_added] = 0 OR [book_quantity] = 0");
                    table.CheckConstraint("ck_inv_stocktake_line_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_inv_stocktake_line_quantity", "[book_quantity] >= 0 AND ([counted_quantity] IS NULL OR [counted_quantity] >= 0)");
                    table.CheckConstraint("ck_inv_stocktake_line_tracking_mode", "[tracking_mode] IN ('None', 'Lot', 'Serial')");
                    table.ForeignKey(
                        name: "fk_inv_stocktake_line_inv_stocktake_company_id_stocktake_id",
                        columns: x => new { x.company_id, x.stocktake_id },
                        principalTable: "inv_stocktake",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inv_stocktake_line_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_id = table.Column<long>(type: "bigint", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    book_quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    counted_quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_stocktake_line_tracking", x => x.id);
                    table.CheckConstraint("ck_inv_stocktake_line_tracking_quantity", "[book_quantity] >= 0 AND ([counted_quantity] IS NULL OR [counted_quantity] >= 0)");
                    table.CheckConstraint("ck_inv_stocktake_line_tracking_serial", "[serial_no] IS NULL OR ([book_quantity] IN (0, 1) AND ([counted_quantity] IS NULL OR [counted_quantity] IN (0, 1)))");
                    table.CheckConstraint("ck_inv_stocktake_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_inv_stocktake_line_tracking_inv_stocktake_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "inv_stocktake_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_company_id_business_date_id",
                table: "inv_stocktake",
                columns: new[] { "company_id", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_company_id_document_no",
                table: "inv_stocktake",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_company_id_status_id",
                table: "inv_stocktake",
                columns: new[] { "company_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_line_company_id_item_id_warehouse_id",
                table: "inv_stocktake_line",
                columns: new[] { "company_id", "item_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_line_company_id_stocktake_id_item_id_warehouse_id_location_id",
                table: "inv_stocktake_line",
                columns: new[] { "company_id", "stocktake_id", "item_id", "warehouse_id", "location_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_line_company_id_stocktake_id_line_no",
                table: "inv_stocktake_line",
                columns: new[] { "company_id", "stocktake_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_line_tracking_company_id_line_id_lot_no",
                table: "inv_stocktake_line_tracking",
                columns: new[] { "company_id", "line_id", "lot_no" },
                unique: true,
                filter: "[lot_no] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_line_tracking_company_id_line_id_seq_no",
                table: "inv_stocktake_line_tracking",
                columns: new[] { "company_id", "line_id", "seq_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stocktake_line_tracking_company_id_line_id_serial_no",
                table: "inv_stocktake_line_tracking",
                columns: new[] { "company_id", "line_id", "serial_no" },
                unique: true,
                filter: "[serial_no] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inv_stocktake_line_tracking");

            migrationBuilder.DropTable(
                name: "inv_stocktake_line");

            migrationBuilder.DropTable(
                name: "inv_stocktake");
        }
    }
}
