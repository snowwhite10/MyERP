using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_OrderCancelReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "pur_purchase_order",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_order_cancel_reason",
                table: "pur_purchase_order",
                sql: "[cancel_reason] IS NULL OR [status] = 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_order_cancel_reason",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                table: "pur_purchase_order");
        }
    }
}
