using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF generated index arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_Valuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_movement_valuation",
                table: "inv_stock_movement",
                columns: new[] { "company_id", "item_id", "posted_at", "id" })
                .Annotation("SqlServer:Include", new[] { "direction", "quantity", "unit_cost", "source_type", "source_id", "source_line_no" });
            migrationBuilder.CreateOrAlterProcedure("inv/usp_inv_value_item");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropProcedure("inv/usp_inv_value_item");
            migrationBuilder.DropIndex(
                name: "ix_inv_stock_movement_valuation",
                table: "inv_stock_movement");
        }
    }
}
