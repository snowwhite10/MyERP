using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_InvoicePosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "fi_journal_id",
                table: "pur_purchase_invoice",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "fi_payable_id",
                table: "pur_purchase_invoice",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "fi_purchase_source_id",
                table: "pur_purchase_invoice",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "posted_at",
                table: "pur_purchase_invoice",
                type: "datetime2(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "posted_from_version",
                table: "pur_purchase_invoice",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_invoice_posted",
                table: "pur_purchase_invoice",
                sql: "([status] = 'Posted' AND [posted_at] IS NOT NULL AND ISNULL([posted_from_version], 0) > 0 AND ISNULL([fi_purchase_source_id], 0) > 0 AND ISNULL([fi_payable_id], 0) > 0 AND ISNULL([fi_journal_id], 0) > 0) OR ([status] <> 'Posted' AND [posted_at] IS NULL AND [posted_from_version] IS NULL AND [fi_purchase_source_id] IS NULL AND [fi_payable_id] IS NULL AND [fi_journal_id] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_invoice_posted",
                table: "pur_purchase_invoice");

            migrationBuilder.DropColumn(
                name: "fi_journal_id",
                table: "pur_purchase_invoice");

            migrationBuilder.DropColumn(
                name: "fi_payable_id",
                table: "pur_purchase_invoice");

            migrationBuilder.DropColumn(
                name: "fi_purchase_source_id",
                table: "pur_purchase_invoice");

            migrationBuilder.DropColumn(
                name: "posted_at",
                table: "pur_purchase_invoice");

            migrationBuilder.DropColumn(
                name: "posted_from_version",
                table: "pur_purchase_invoice");
        }
    }
}
