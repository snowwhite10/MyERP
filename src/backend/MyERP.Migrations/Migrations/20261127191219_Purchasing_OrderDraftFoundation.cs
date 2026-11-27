using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_OrderDraftFoundation : Migration
    {
        private static readonly string[] OrderQueryColumns = ["company_id", "status", "business_date", "id"];
        private static readonly string[] RequisitionSourceColumns = ["company_id", "requisition_line_id"];
        private static readonly string[] RequisitionIdentityColumns = ["company_id", "id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "original_gross",
                table: "pur_purchase_order_line",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "original_net",
                table: "pur_purchase_order_line",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "original_tax",
                table: "pur_purchase_order_line",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remark",
                table: "pur_purchase_order",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requisition_consumption_active",
                table: "pur_purchase_order",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_order_line_original_amount",
                table: "pur_purchase_order_line",
                sql: "([original_net] IS NULL AND [original_tax] IS NULL AND [original_gross] IS NULL) OR ([original_net] >= 0 AND [original_tax] >= 0 AND [original_gross] = [original_net] + [original_tax])");

            migrationBuilder.CreateIndex(
                name: "ix_pur_purchase_order_company_id_status_business_date_id",
                table: "pur_purchase_order",
                columns: OrderQueryColumns);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_order_consumption_state",
                table: "pur_purchase_order",
                sql: "[requisition_consumption_active] = 0 OR ([is_deleted] = 0 AND [status] IN ('PendingApproval','Approved'))");

            migrationBuilder.AddForeignKey(
                name: "fk_pur_purchase_order_line_pur_requisition_line_company_id_requisition_line_id",
                table: "pur_purchase_order_line",
                columns: RequisitionSourceColumns,
                principalTable: "pur_requisition_line",
                principalColumns: RequisitionIdentityColumns,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pur_purchase_order_line_pur_requisition_line_company_id_requisition_line_id",
                table: "pur_purchase_order_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_order_line_original_amount",
                table: "pur_purchase_order_line");

            migrationBuilder.DropIndex(
                name: "ix_pur_purchase_order_company_id_status_business_date_id",
                table: "pur_purchase_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_order_consumption_state",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "original_gross",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "original_net",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "original_tax",
                table: "pur_purchase_order_line");

            migrationBuilder.DropColumn(
                name: "remark",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "requisition_consumption_active",
                table: "pur_purchase_order");
        }
    }
}
