using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_ReceiptDraftSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                table: "pur_purchase_receipt_line",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "branch_id",
                table: "pur_purchase_receipt",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "department_id",
                table: "pur_purchase_receipt",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "owner_id",
                table: "pur_purchase_receipt",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_receipt_company_id_owner_id_id",
                table: "pur_purchase_receipt",
                columns: ["company_id", "owner_id", "id"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_receipt_company_id_owner_id_id",
                table: "pur_purchase_receipt");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "pur_purchase_receipt_line");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "pur_purchase_receipt");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "pur_purchase_receipt");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "pur_purchase_receipt");
        }
    }
}
