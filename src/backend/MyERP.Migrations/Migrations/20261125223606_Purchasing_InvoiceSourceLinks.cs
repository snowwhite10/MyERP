using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration column arrays are emitted once.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_InvoiceSourceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_pur_purchase_receipt_line_company_id_id",
                table: "pur_purchase_receipt_line",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_pur_purchase_invoice_order",
                table: "pur_purchase_invoice",
                columns: new[] { "company_id", "purchase_order_id" },
                principalTable: "pur_purchase_order",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pur_purchase_invoice_line_receipt_line",
                table: "pur_purchase_invoice_line",
                columns: new[] { "company_id", "receipt_line_id" },
                principalTable: "pur_purchase_receipt_line",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pur_purchase_invoice_order",
                table: "pur_purchase_invoice");

            migrationBuilder.DropForeignKey(
                name: "fk_pur_purchase_invoice_line_receipt_line",
                table: "pur_purchase_invoice_line");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pur_purchase_receipt_line_company_id_id",
                table: "pur_purchase_receipt_line");
        }
    }
}
