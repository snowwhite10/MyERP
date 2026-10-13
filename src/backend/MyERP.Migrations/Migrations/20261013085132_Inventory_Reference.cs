using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF 產生的 migration 索引欄位陣列

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Inventory_Reference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inv_reference",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    source_key = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    item_code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    location_code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_reference", x => x.id);
                    table.CheckConstraint("ck_inv_reference_location", "([location_id] IS NULL AND [location_code] IS NULL) OR ([location_id] IS NOT NULL AND [location_code] IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_reference_company_id_item_code",
                table: "inv_reference",
                columns: new[] { "company_id", "item_code" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_reference_company_id_item_id",
                table: "inv_reference",
                columns: new[] { "company_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_reference_company_id_location_id",
                table: "inv_reference",
                columns: new[] { "company_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_reference_company_id_source_key",
                table: "inv_reference",
                columns: new[] { "company_id", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_reference_company_id_warehouse_code_location_code",
                table: "inv_reference",
                columns: new[] { "company_id", "warehouse_code", "location_code" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_reference_company_id_warehouse_id",
                table: "inv_reference",
                columns: new[] { "company_id", "warehouse_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inv_reference");
        }
    }
}
