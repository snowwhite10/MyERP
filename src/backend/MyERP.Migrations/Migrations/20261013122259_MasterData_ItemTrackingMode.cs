using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_ItemTrackingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tracking_mode",
                table: "md_item",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddCheckConstraint(
                name: "ck_md_item_tracking_mode",
                table: "md_item",
                sql: "[tracking_mode] IN ('None', 'Lot', 'Serial')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_md_item_tracking_mode",
                table: "md_item");

            migrationBuilder.DropColumn(
                name: "tracking_mode",
                table: "md_item");
        }
    }
}
