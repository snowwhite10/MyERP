using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration column arrays are emitted once.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_InvoiceListDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_invoice_company_id_business_date_id",
                table: "pur_purchase_invoice",
                columns: new[] { "company_id", "business_date", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_invoice_company_id_business_date_id",
                table: "pur_purchase_invoice");
        }
    }
}
