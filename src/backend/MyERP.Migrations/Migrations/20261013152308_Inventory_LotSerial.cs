using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF 產生的 migration 索引欄位陣列

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_LotSerial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_inv_stock_movement_company_id_id",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_inv_other_receipt_line_company_id_id",
                table: "inv_other_receipt_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_inv_other_issue_line_company_id_id",
                table: "inv_other_issue_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_inv_opening_stock_line_company_id_id",
                table: "inv_opening_stock_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "inv_lot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    manufacture_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_lot", x => x.id);
                    table.UniqueConstraint("ak_inv_lot_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_lot_dates", "[expiry_date] IS NULL OR [manufacture_date] IS NULL OR [expiry_date] >= [manufacture_date]");
                    table.CheckConstraint("ck_inv_lot_lot_no", "LEN([lot_no]) > 0");
                });

            migrationBuilder.CreateTable(
                name: "inv_opening_stock_line_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_id = table.Column<long>(type: "bigint", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    manufacture_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_opening_stock_line_tracking", x => x.id);
                    table.CheckConstraint("ck_inv_opening_stock_line_tracking_dates", "[expiry_date] IS NULL OR [manufacture_date] IS NULL OR [expiry_date] >= [manufacture_date]");
                    table.CheckConstraint("ck_inv_opening_stock_line_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_opening_stock_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1 AND [manufacture_date] IS NULL AND [expiry_date] IS NULL)");
                    table.ForeignKey(
                        name: "fk_inv_opening_stock_line_tracking_inv_opening_stock_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "inv_opening_stock_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inv_other_issue_line_tracking",
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
                    table.PrimaryKey("pk_inv_other_issue_line_tracking", x => x.id);
                    table.CheckConstraint("ck_inv_other_issue_line_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_other_issue_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1)");
                    table.ForeignKey(
                        name: "fk_inv_other_issue_line_tracking_inv_other_issue_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "inv_other_issue_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inv_other_receipt_line_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_id = table.Column<long>(type: "bigint", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    manufacture_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_other_receipt_line_tracking", x => x.id);
                    table.CheckConstraint("ck_inv_other_receipt_line_tracking_dates", "[expiry_date] IS NULL OR [manufacture_date] IS NULL OR [expiry_date] >= [manufacture_date]");
                    table.CheckConstraint("ck_inv_other_receipt_line_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_other_receipt_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND [quantity] = 1 AND [manufacture_date] IS NULL AND [expiry_date] IS NULL)");
                    table.ForeignKey(
                        name: "fk_inv_other_receipt_line_tracking_inv_other_receipt_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "inv_other_receipt_line",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inv_serial",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    serial_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_serial", x => x.id);
                    table.UniqueConstraint("ak_inv_serial_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_inv_serial_serial_no", "LEN([serial_no]) > 0");
                });

            migrationBuilder.CreateTable(
                name: "inv_stock_movement_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    movement_id = table.Column<long>(type: "bigint", nullable: false),
                    lot_id = table.Column<long>(type: "bigint", nullable: true),
                    serial_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_inv_stock_movement_tracking", x => x.id);
                    table.CheckConstraint("ck_inv_stock_movement_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_inv_stock_movement_tracking_target", "([lot_id] IS NOT NULL AND [serial_id] IS NULL) OR ([lot_id] IS NULL AND [serial_id] IS NOT NULL AND [quantity] = 1)");
                    table.ForeignKey(
                        name: "fk_inv_stock_movement_tracking_inv_lot_company_id_lot_id",
                        columns: x => new { x.company_id, x.lot_id },
                        principalTable: "inv_lot",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inv_stock_movement_tracking_inv_serial_company_id_serial_id",
                        columns: x => new { x.company_id, x.serial_id },
                        principalTable: "inv_serial",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inv_stock_movement_tracking_inv_stock_movement_company_id_movement_id",
                        columns: x => new { x.company_id, x.movement_id },
                        principalTable: "inv_stock_movement",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_lot_company_id_item_id_lot_no",
                table: "inv_lot",
                columns: new[] { "company_id", "item_id", "lot_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_opening_stock_line_tracking_company_id_line_id_seq_no",
                table: "inv_opening_stock_line_tracking",
                columns: new[] { "company_id", "line_id", "seq_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_other_issue_line_tracking_company_id_line_id_seq_no",
                table: "inv_other_issue_line_tracking",
                columns: new[] { "company_id", "line_id", "seq_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_other_receipt_line_tracking_company_id_line_id_seq_no",
                table: "inv_other_receipt_line_tracking",
                columns: new[] { "company_id", "line_id", "seq_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_serial_company_id_item_id_serial_no",
                table: "inv_serial",
                columns: new[] { "company_id", "item_id", "serial_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_tracking_company_id_lot_id",
                table: "inv_stock_movement_tracking",
                columns: new[] { "company_id", "lot_id" })
                .Annotation("SqlServer:Include", new[] { "movement_id", "quantity" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_tracking_company_id_movement_id",
                table: "inv_stock_movement_tracking",
                columns: new[] { "company_id", "movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_tracking_company_id_serial_id",
                table: "inv_stock_movement_tracking",
                columns: new[] { "company_id", "serial_id" })
                .Annotation("SqlServer:Include", new[] { "movement_id", "quantity" });

            // 批序號明細與異動帳一樣只增不改（ADR-060 第 6 點）：更正以反向單據追加
            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER trg_inv_stock_movement_tracking_immutable ON inv_stock_movement_tracking
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51001, 'inv_stock_movement_tracking is append-only; post a reversing source instead.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_inv_stock_movement_tracking_immutable;");

            migrationBuilder.DropTable(
                name: "inv_opening_stock_line_tracking");

            migrationBuilder.DropTable(
                name: "inv_other_issue_line_tracking");

            migrationBuilder.DropTable(
                name: "inv_other_receipt_line_tracking");

            migrationBuilder.DropTable(
                name: "inv_stock_movement_tracking");

            migrationBuilder.DropTable(
                name: "inv_lot");

            migrationBuilder.DropTable(
                name: "inv_serial");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_inv_stock_movement_company_id_id",
                table: "inv_stock_movement");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_inv_other_receipt_line_company_id_id",
                table: "inv_other_receipt_line");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_inv_other_issue_line_company_id_id",
                table: "inv_other_issue_line");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_inv_opening_stock_line_company_id_id",
                table: "inv_opening_stock_line");
        }
    }
}
