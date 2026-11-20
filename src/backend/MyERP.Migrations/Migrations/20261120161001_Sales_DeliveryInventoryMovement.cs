using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generates constant column arrays for migration operations.

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Sales_DeliveryInventoryMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "inv_movement_id",
                table: "sal_delivery_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sal_delivery_line_company_id_inv_movement_id",
                table: "sal_delivery_line",
                columns: new[] { "company_id", "inv_movement_id" },
                unique: true,
                filter: "[inv_movement_id] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sal_delivery_line_inv_movement",
                table: "sal_delivery_line",
                sql: "[inv_movement_id] IS NULL OR [inv_movement_id] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sal_delivery_line_company_id_inv_movement_id",
                table: "sal_delivery_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sal_delivery_line_inv_movement",
                table: "sal_delivery_line");

            migrationBuilder.DropColumn(
                name: "inv_movement_id",
                table: "sal_delivery_line");
        }
    }
}
