using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_InvoiceClaimIdentityLink : Migration
    {
        private static readonly string[] InvoiceReferenceColumns = ["company_id", "invoice_id"];
        private static readonly string[] InvoiceKeyColumns = ["company_id", "id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_pur_purchase_invoice_number_claim_purchase_invoice_company_id_invoice_id",
                table: "pur_purchase_invoice_number_claim",
                columns: InvoiceReferenceColumns,
                principalTable: "pur_purchase_invoice",
                principalColumns: InvoiceKeyColumns,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pur_purchase_invoice_number_claim_purchase_invoice_company_id_invoice_id",
                table: "pur_purchase_invoice_number_claim");
        }
    }
}
