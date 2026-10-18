using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core generates literal arrays for composite indexes.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_StockFlowIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_flow_report",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "business_date", "item_id", "warehouse_id", "location_id" })
                .Annotation("SqlServer:Include", new[] { "direction", "quantity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inv_stock_movement_flow_report",
                table: "inv_stock_movement");
        }
    }
}
