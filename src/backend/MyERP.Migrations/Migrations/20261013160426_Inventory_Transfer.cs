using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF 產生的 migration 索引欄位陣列

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_Transfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inv_transfer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_inv_transfer", x => x.id);
                    table.UniqueConstraint("ak_inv_transfer_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_transfer_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_inv_transfer_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_inv_transfer_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
                    table.CheckConstraint("ck_inv_transfer_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Posted')");
                });

            migrationBuilder.CreateTable(
                name: "inv_transfer_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transfer_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    source_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    source_location_id = table.Column<long>(type: "bigint", nullable: true),
                    destination_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    destination_location_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_transfer_line", x => x.id);
                    table.UniqueConstraint("ak_inv_transfer_line_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("ck_inv_transfer_line_distinct", "[source_warehouse_id] <> [destination_warehouse_id] OR ISNULL([source_location_id], -1) <> ISNULL([destination_location_id], -1)");
                    table.CheckConstraint("ck_inv_transfer_line_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_inv_transfer_line_quantity", "[quantity] > 0");
                    table.ForeignKey(
                        name: "fk_inv_transfer_line_inv_transfer_company_id_transfer_id",
                        columns: x => new { x.company_id, x.transfer_id },
                        principalTable: "inv_transfer",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inv_transfer_line_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_id = table.Column<long>(type: "bigint", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_transfer_line_tracking", x => x.id);
                    table.CheckConstraint("ck_inv_transfer_line_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_transfer_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1)");
                    table.ForeignKey(
                        name: "fk_inv_transfer_line_tracking_inv_transfer_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "inv_transfer_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_company_id_business_date_id",
                table: "inv_transfer",
                columns: new[] { "company_id", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_company_id_document_no",
                table: "inv_transfer",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_company_id_status_id",
                table: "inv_transfer",
                columns: new[] { "company_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_line_company_id_destination_warehouse_id_destination_location_id",
                table: "inv_transfer_line",
                columns: new[] { "company_id", "destination_warehouse_id", "destination_location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_line_company_id_item_id",
                table: "inv_transfer_line",
                columns: new[] { "company_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_line_company_id_source_warehouse_id_source_location_id",
                table: "inv_transfer_line",
                columns: new[] { "company_id", "source_warehouse_id", "source_location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_line_company_id_transfer_id_line_no",
                table: "inv_transfer_line",
                columns: new[] { "company_id", "transfer_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_transfer_line_tracking_company_id_line_id_seq_no",
                table: "inv_transfer_line_tracking",
                columns: new[] { "company_id", "line_id", "seq_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inv_transfer_line_tracking");

            migrationBuilder.DropTable(
                name: "inv_transfer_line");

            migrationBuilder.DropTable(
                name: "inv_transfer");
        }
    }
}
