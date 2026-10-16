using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core generates literal arrays for composite indexes.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_SafetyStockPagingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_inv_safety_stock_threshold_company_id_id",
                table: "inv_safety_stock_threshold",
                columns: new[] { "company_id", "id" })
                .Annotation("SqlServer:Include", new[] { "is_deleted", "item_id", "warehouse_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inv_safety_stock_threshold_company_id_id",
                table: "inv_safety_stock_threshold");
        }
    }
}
