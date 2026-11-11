using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyERP.Shared.Infrastructure.Persistence;

#pragma warning disable CA1861 // Migration column arrays are emitted once by EF-style migration code.

namespace MyERP.Migrations.Migrations;

[DbContext(typeof(MyErpDbContext))]
[Migration("20261112064200_Purchasing_InvoiceNumberClaims")]
public sealed class Purchasing_InvoiceNumberClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pur_purchase_invoice_number_claim",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                company_id = table.Column<long>(type: "bigint", nullable: false),
                supplier_id = table.Column<long>(type: "bigint", nullable: false),
                invoice_id = table.Column<long>(type: "bigint", nullable: false),
                number_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                is_active = table.Column<bool>(type: "bit", nullable: false),
                created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                created_by = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                updated_by = table.Column<long>(type: "bigint", nullable: true),
                version = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pur_purchase_invoice_number_claim", x => x.id);
                table.CheckConstraint("ck_pur_invoice_number_claim_refs", "[company_id] > 0 AND [supplier_id] > 0 AND [invoice_id] > 0");
                table.CheckConstraint("ck_pur_invoice_number_claim_key", "LEN([number_key]) > 0");
            });

        migrationBuilder.CreateIndex(
            name: "ux_pur_purchase_invoice_number_claim_company_id_supplier_id_number_key",
            table: "pur_purchase_invoice_number_claim",
            columns: new[] { "company_id", "supplier_id", "number_key" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "ux_pur_purchase_invoice_number_claim_company_id_invoice_id_number_key",
            table: "pur_purchase_invoice_number_claim",
            columns: new[] { "company_id", "invoice_id", "number_key" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "ux_pur_purchase_invoice_number_claim_company_id_invoice_id_is_active",
            table: "pur_purchase_invoice_number_claim",
            columns: new[] { "company_id", "invoice_id", "is_active" }, unique: true,
            filter: "[is_active] = 1");
        migrationBuilder.CreateIndex(
            name: "ix_pur_purchase_invoice_number_claim_company_id_supplier_id_invoice_id",
            table: "pur_purchase_invoice_number_claim",
            columns: new[] { "company_id", "supplier_id", "invoice_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "pur_purchase_invoice_number_claim");
}
