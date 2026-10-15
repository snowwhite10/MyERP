using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core generates literal arrays for composite indexes.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_AssemblyDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inv_assembly",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    document_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
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
                    table.PrimaryKey("pk_inv_assembly", x => x.id);
                    table.UniqueConstraint("ak_inv_assembly_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_assembly_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_inv_assembly_deleted", "([is_deleted] = 0 AND [deleted_at] IS NULL) OR ([is_deleted] = 1 AND [deleted_at] IS NOT NULL AND [status] = 'Draft')");
                    table.CheckConstraint("ck_inv_assembly_posted", "([status] = 'Posted' AND [posted_at] IS NOT NULL) OR ([status] <> 'Posted' AND [posted_at] IS NULL)");
                    table.CheckConstraint("ck_inv_assembly_status", "[status] IN ('Draft', 'PendingApproval', 'Rejected', 'Posted')");
                });

            migrationBuilder.CreateTable(
                name: "inv_assembly_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    assembly_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    role = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    posted_unit_cost = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_assembly_line", x => x.id);
                    table.UniqueConstraint("ak_inv_assembly_line_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_assembly_line_cost", "[posted_unit_cost] IS NULL OR [posted_unit_cost] >= 0");
                    table.CheckConstraint("ck_inv_assembly_line_no", "[line_no] > 0");
                    table.CheckConstraint("ck_inv_assembly_line_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_assembly_line_role", "[role] IN ('Finished', 'Component')");
                    table.ForeignKey(
                        name: "fk_inv_assembly_line_inv_assembly_company_id_assembly_id",
                        columns: x => new { x.company_id, x.assembly_id },
                        principalTable: "inv_assembly",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inv_assembly_line_tracking",
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
                    table.PrimaryKey("pk_inv_assembly_line_tracking", x => x.id);
                    table.CheckConstraint("ck_inv_assembly_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_assembly_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1)");
                    table.ForeignKey(
                        name: "fk_inv_assembly_line_tracking_inv_assembly_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "inv_assembly_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_company_id_business_date_id",
                table: "inv_assembly",
                columns: new[] { "company_id", "business_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_company_id_document_no",
                table: "inv_assembly",
                columns: new[] { "company_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_company_id_status_id",
                table: "inv_assembly",
                columns: new[] { "company_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_line_company_id_assembly_id_line_no",
                table: "inv_assembly_line",
                columns: new[] { "company_id", "assembly_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_line_company_id_assembly_id_role",
                table: "inv_assembly_line",
                columns: new[] { "company_id", "assembly_id", "role" },
                unique: true,
                filter: "[role] = 'Finished'");

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_line_company_id_item_id_warehouse_id",
                table: "inv_assembly_line",
                columns: new[] { "company_id", "item_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_line_company_id_warehouse_id_location_id",
                table: "inv_assembly_line",
                columns: new[] { "company_id", "warehouse_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_line_tracking_company_id_line_id_lot_no",
                table: "inv_assembly_line_tracking",
                columns: new[] { "company_id", "line_id", "lot_no" },
                unique: true,
                filter: "[lot_no] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_line_tracking_company_id_line_id_seq_no",
                table: "inv_assembly_line_tracking",
                columns: new[] { "company_id", "line_id", "seq_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_assembly_line_tracking_company_id_line_id_serial_no",
                table: "inv_assembly_line_tracking",
                columns: new[] { "company_id", "line_id", "serial_no" },
                unique: true,
                filter: "[serial_no] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inv_assembly_line_tracking");

            migrationBuilder.DropTable(
                name: "inv_assembly_line");

            migrationBuilder.DropTable(
                name: "inv_assembly");
        }
    }
}
