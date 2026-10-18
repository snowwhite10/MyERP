using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core migration 使用常值欄位陣列。

namespace MyERP.Migrations.Migrations;

/// <summary>覆蓋批序號追溯所需欄位，避免由異動帳排序時對每筆明細回表。</summary>
public partial class Inventory_TrackingExportCoveringIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_inv_stock_movement_tracking_company_id_movement_id",
            table: "inv_stock_movement_tracking");
        migrationBuilder.CreateIndex(
            name: "ix_inv_stock_movement_tracking_company_id_movement_id",
            table: "inv_stock_movement_tracking",
            columns: new[] { "company_id", "movement_id" })
            .Annotation("SqlServer:Include", new[] { "lot_id", "serial_id", "quantity" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_inv_stock_movement_tracking_company_id_movement_id",
            table: "inv_stock_movement_tracking");
        migrationBuilder.CreateIndex(
            name: "ix_inv_stock_movement_tracking_company_id_movement_id",
            table: "inv_stock_movement_tracking",
            columns: new[] { "company_id", "movement_id" });
    }
}
