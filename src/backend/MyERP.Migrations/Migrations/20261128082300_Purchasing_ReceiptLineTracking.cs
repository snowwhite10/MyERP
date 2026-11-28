using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_ReceiptLineTracking : Migration
    {
        private static readonly string[] LinePrincipalColumns = ["company_id", "id"];
        private static readonly string[] TrackingIndexColumns = ["company_id", "line_id", "seq_no"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pur_purchase_receipt_line_tracking",
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
                    table.PrimaryKey("pk_pur_purchase_receipt_line_tracking", x => x.id);
                    table.CheckConstraint("ck_pur_receipt_line_tracking_dates", "[manufacture_date] IS NULL OR [expiry_date] IS NULL OR [expiry_date] >= [manufacture_date]");
                    table.CheckConstraint("ck_pur_receipt_line_tracking_quantity", "[quantity] > 0");
                    table.CheckConstraint("ck_pur_receipt_line_tracking_seq_no", "[seq_no] > 0");
                    table.CheckConstraint("ck_pur_receipt_line_tracking_target", "([lot_no] IS NOT NULL AND [serial_no] IS NULL AND LEN([lot_no]) > 0) OR ([lot_no] IS NULL AND [serial_no] IS NOT NULL AND LEN([serial_no]) > 0 AND [quantity] = 1 AND [manufacture_date] IS NULL AND [expiry_date] IS NULL)");
                    table.ForeignKey(
                        name: "fk_pur_purchase_receipt_line_tracking_pur_purchase_receipt_line_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "pur_purchase_receipt_line",
                        principalColumns: LinePrincipalColumns,
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_receipt_line_tracking_company_id_line_id_seq_no",
                table: "pur_purchase_receipt_line_tracking",
                columns: TrackingIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pur_purchase_receipt_line_tracking");
        }
    }
}
