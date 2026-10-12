using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_PartnerPaymentDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "default_payment_term_id",
                table: "md_supplier",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "default_settlement_method_id",
                table: "md_supplier",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "default_payment_term_id",
                table: "md_customer",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "default_settlement_method_id",
                table: "md_customer",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_settlement_method_company_id_id",
                table: "md_settlement_method",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_payment_term_company_id_id",
                table: "md_payment_term",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_supplier_company_id_default_payment_term_id",
                table: "md_supplier",
                columns: new[] { "company_id", "default_payment_term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_supplier_company_id_default_settlement_method_id",
                table: "md_supplier",
                columns: new[] { "company_id", "default_settlement_method_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_customer_company_id_default_payment_term_id",
                table: "md_customer",
                columns: new[] { "company_id", "default_payment_term_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_customer_company_id_default_settlement_method_id",
                table: "md_customer",
                columns: new[] { "company_id", "default_settlement_method_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_md_customer_payment_term_company_id_default_payment_term_id",
                table: "md_customer",
                columns: new[] { "company_id", "default_payment_term_id" },
                principalTable: "md_payment_term",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_md_customer_settlement_method_company_id_default_settlement_method_id",
                table: "md_customer",
                columns: new[] { "company_id", "default_settlement_method_id" },
                principalTable: "md_settlement_method",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_md_supplier_payment_term_company_id_default_payment_term_id",
                table: "md_supplier",
                columns: new[] { "company_id", "default_payment_term_id" },
                principalTable: "md_payment_term",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_md_supplier_settlement_method_company_id_default_settlement_method_id",
                table: "md_supplier",
                columns: new[] { "company_id", "default_settlement_method_id" },
                principalTable: "md_settlement_method",
                principalColumns: new[] { "company_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_md_customer_payment_term_company_id_default_payment_term_id",
                table: "md_customer");

            migrationBuilder.DropForeignKey(
                name: "fk_md_customer_settlement_method_company_id_default_settlement_method_id",
                table: "md_customer");

            migrationBuilder.DropForeignKey(
                name: "fk_md_supplier_payment_term_company_id_default_payment_term_id",
                table: "md_supplier");

            migrationBuilder.DropForeignKey(
                name: "fk_md_supplier_settlement_method_company_id_default_settlement_method_id",
                table: "md_supplier");

            migrationBuilder.DropIndex(
                name: "ix_md_supplier_company_id_default_payment_term_id",
                table: "md_supplier");

            migrationBuilder.DropIndex(
                name: "ix_md_supplier_company_id_default_settlement_method_id",
                table: "md_supplier");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_settlement_method_company_id_id",
                table: "md_settlement_method");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_payment_term_company_id_id",
                table: "md_payment_term");

            migrationBuilder.DropIndex(
                name: "ix_md_customer_company_id_default_payment_term_id",
                table: "md_customer");

            migrationBuilder.DropIndex(
                name: "ix_md_customer_company_id_default_settlement_method_id",
                table: "md_customer");

            migrationBuilder.DropColumn(
                name: "default_payment_term_id",
                table: "md_supplier");

            migrationBuilder.DropColumn(
                name: "default_settlement_method_id",
                table: "md_supplier");

            migrationBuilder.DropColumn(
                name: "default_payment_term_id",
                table: "md_customer");

            migrationBuilder.DropColumn(
                name: "default_settlement_method_id",
                table: "md_customer");
        }
    }
}
