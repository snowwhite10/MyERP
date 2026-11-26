using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Purchasing_OrderRateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "base_currency_code",
                table: "pur_purchase_order",
                type: "char(3)",
                unicode: false,
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "pur_purchase_order",
                type: "decimal(19,8)",
                precision: 19,
                scale: 8,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pur_order_rate_snapshot",
                table: "pur_purchase_order",
                sql: "([base_currency_code] IS NULL AND [exchange_rate] IS NULL) OR ([base_currency_code] IS NOT NULL AND [exchange_rate] IS NOT NULL AND [exchange_rate] > 0 AND ([currency] <> [base_currency_code] OR [exchange_rate] = 1))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pur_order_rate_snapshot",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "base_currency_code",
                table: "pur_purchase_order");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "pur_purchase_order");
        }
    }
}
