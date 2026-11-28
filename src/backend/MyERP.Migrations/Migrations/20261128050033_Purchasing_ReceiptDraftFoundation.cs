using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_ReceiptDraftFoundation : Migration
    {
        private static readonly string[] ReceiptDocumentIndexColumns = ["company_id", "document_no"];
        private static readonly string[] ReceiptWarehouseIndexColumns = ["company_id", "warehouse_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_receipt_line_quantities",
                table: "pur_purchase_receipt_line");

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                table: "pur_purchase_receipt_line",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "warehouse_id",
                table: "pur_purchase_receipt_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "business_date",
                table: "pur_purchase_receipt",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_no",
                table: "pur_purchase_receipt",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pending_receipt_reserved",
                table: "pur_purchase_order_line",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "posted_received",
                table: "pur_purchase_order_line",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE order_line SET posted_received = received.total_quantity
                FROM dbo.pur_purchase_order_line AS order_line
                INNER JOIN (
                    SELECT receipt_line.company_id, receipt_line.purchase_order_line_id,
                           SUM(receipt_line.posted_received) AS total_quantity
                    FROM dbo.pur_purchase_receipt_line AS receipt_line
                    INNER JOIN dbo.pur_purchase_receipt AS receipt
                        ON receipt.company_id = receipt_line.company_id
                       AND receipt.id = receipt_line.purchase_receipt_id
                    WHERE receipt.status = 'Posted' AND receipt.is_deleted = 0
                    GROUP BY receipt_line.company_id, receipt_line.purchase_order_line_id
                ) AS received
                    ON received.company_id = order_line.company_id
                   AND received.purchase_order_line_id = order_line.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_receipt_line_company_id_warehouse_id",
                table: "pur_purchase_receipt_line",
                columns: ReceiptWarehouseIndexColumns);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_receipt_line_quantities",
                table: "pur_purchase_receipt_line",
                sql: "((([quantity] IS NULL AND [posted_received] > 0) OR ([quantity] IS NOT NULL AND [quantity] > 0 AND [warehouse_id] IS NOT NULL AND [warehouse_id] > 0 AND [posted_received] >= 0 AND [posted_received] <= [quantity])) AND [posted_returned] >= 0 AND [pending_return_reserved] >= 0 AND [posted_invoiced] >= 0 AND [matched_reserved] >= 0 AND [posted_returned]+[pending_return_reserved]+[posted_invoiced]+[matched_reserved] <= [posted_received])");

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_receipt_company_id_document_no",
                table: "pur_purchase_receipt",
                columns: ReceiptDocumentIndexColumns,
                unique: true,
                filter: "[document_no] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_order_line_receipt_capacity",
                table: "pur_purchase_order_line",
                sql: "[posted_received] >= 0 AND [pending_receipt_reserved] >= 0 AND [posted_received] + [pending_receipt_reserved] <= [quantity]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_receipt_line_company_id_warehouse_id",
                table: "pur_purchase_receipt_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_receipt_line_quantities",
                table: "pur_purchase_receipt_line");

            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_receipt_company_id_document_no",
                table: "pur_purchase_receipt");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_order_line_receipt_capacity",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "pur_purchase_receipt_line");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                table: "pur_purchase_receipt_line");

            migrationBuilder.DropColumn(
                name: "business_date",
                table: "pur_purchase_receipt");

            migrationBuilder.DropColumn(
                name: "document_no",
                table: "pur_purchase_receipt");

            migrationBuilder.DropColumn(
                name: "pending_receipt_reserved",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "posted_received",
                table: "pur_purchase_order_line");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_receipt_line_quantities",
                table: "pur_purchase_receipt_line",
                sql: "[posted_received] > 0 AND [posted_returned] >= 0 AND [pending_return_reserved] >= 0 AND [posted_invoiced] >= 0 AND [matched_reserved] >= 0 AND [posted_returned]+[pending_return_reserved]+[posted_invoiced]+[matched_reserved] <= [posted_received]");
        }
    }
}
