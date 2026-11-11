using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyERP.Shared.Infrastructure.Persistence;

#pragma warning disable CA1861 // Migration column arrays are emitted once by EF-style migration code.

namespace MyERP.Migrations.Migrations;

[DbContext(typeof(MyErpDbContext))]
[Migration("20261112070500_Purchasing_InvoiceDraft")]
public sealed class Purchasing_InvoiceDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pur_purchase_invoice",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                company_id = table.Column<long>(type: "bigint", nullable: false),
                supplier_id = table.Column<long>(type: "bigint", nullable: false),
                purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                currency = table.Column<string>(type: "char(3)", nullable: false),
                business_date = table.Column<DateOnly>(type: "date", nullable: false),
                supplier_invoice_number = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                number_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, collation: "Latin1_General_100_BIN2"),
                status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                total_net = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                total_tax = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                total_gross = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                is_deleted = table.Column<bool>(type: "bit", nullable: false),
                deleted_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                version = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pur_purchase_invoice", x => x.id);
                table.UniqueConstraint("ak_pur_purchase_invoice_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("ck_pur_invoice_header_refs", "[supplier_id] > 0 AND [purchase_order_id] > 0");
                table.CheckConstraint("ck_pur_invoice_header_totals", "[total_net] >= 0 AND [total_tax] >= 0 AND [total_gross] = [total_net] + [total_tax]");
                table.CheckConstraint("ck_pur_invoice_header_status", "[status] IN ('Draft', 'Matched', 'Disputed', 'Rejected', 'Posted')");
            });
        migrationBuilder.CreateIndex("ix_pur_purchase_invoice_company_id_supplier_id_id", "pur_purchase_invoice",
            new[] { "company_id", "supplier_id", "id" });
        migrationBuilder.CreateIndex("ix_pur_purchase_invoice_company_id_purchase_order_id_status_id", "pur_purchase_invoice",
            new[] { "company_id", "purchase_order_id", "status", "id" });

        migrationBuilder.CreateTable(
            name: "pur_purchase_invoice_line",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                company_id = table.Column<long>(type: "bigint", nullable: false),
                invoice_id = table.Column<long>(type: "bigint", nullable: false),
                line_no = table.Column<int>(type: "int", nullable: false),
                receipt_line_id = table.Column<long>(type: "bigint", nullable: false),
                quantity = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                unit_price = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                tax_rate_percent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                net = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                tax = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                gross = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                version = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pur_purchase_invoice_line", x => x.id);
                table.CheckConstraint("ck_pur_invoice_line_refs", "[invoice_id] > 0 AND [line_no] > 0 AND [receipt_line_id] > 0");
                table.CheckConstraint("ck_pur_invoice_line_amounts", "[quantity] > 0 AND [unit_price] >= 0 AND [tax_rate_percent] BETWEEN 0 AND 100 AND [net] >= 0 AND [tax] >= 0 AND [gross] = [net] + [tax]");
                table.ForeignKey("fk_pur_purchase_invoice_line_pur_purchase_invoice_company_id_invoice_id",
                    x => new { x.company_id, x.invoice_id }, "pur_purchase_invoice", new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("ux_pur_purchase_invoice_line_company_id_invoice_id_line_no", "pur_purchase_invoice_line",
            new[] { "company_id", "invoice_id", "line_no" }, unique: true);
        migrationBuilder.CreateIndex("ix_pur_purchase_invoice_line_company_id_receipt_line_id", "pur_purchase_invoice_line",
            new[] { "company_id", "receipt_line_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "pur_purchase_invoice_line");
        migrationBuilder.DropTable(name: "pur_purchase_invoice");
    }
}
