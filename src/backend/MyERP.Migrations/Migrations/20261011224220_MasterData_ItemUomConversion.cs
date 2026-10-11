using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_ItemUomConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_md_item_company_id_id",
                table: "md_item",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "md_item_uom_conversion",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    uom_id = table.Column<long>(type: "bigint", nullable: false),
                    factor = table.Column<decimal>(type: "decimal(15,6)", precision: 15, scale: 6, nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_item_uom_conversion", x => x.id);
                    table.CheckConstraint("ck_md_item_uom_conversion_factor", "[factor] > 0");
                    table.ForeignKey(
                        name: "fk_md_item_uom_conversion_md_item_company_id_item_id",
                        columns: x => new { x.company_id, x.item_id },
                        principalTable: "md_item",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "fk_md_item_uom_conversion_md_uom_company_id_uom_id",
                        columns: x => new { x.company_id, x.uom_id },
                        principalTable: "md_uom",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_item_uom_conversion_company_id_uom_id",
                table: "md_item_uom_conversion",
                columns: new[] { "company_id", "uom_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_item_uom_conversion_company_id_item_id_uom_id",
                table: "md_item_uom_conversion",
                columns: new[] { "company_id", "item_id", "uom_id" },
                unique: true,
                filter: "[is_deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_item_uom_conversion");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_md_item_company_id_id",
                table: "md_item");
        }
    }
}
