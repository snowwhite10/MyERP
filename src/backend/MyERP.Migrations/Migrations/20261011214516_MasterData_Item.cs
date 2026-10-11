using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Item : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_item",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    specification = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    base_uom_id = table.Column<long>(type: "bigint", nullable: false),
                    brand_id = table.Column<long>(type: "bigint", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_md_item_md_item_brand_company_id_brand_id",
                        columns: x => new { x.company_id, x.brand_id },
                        principalTable: "md_item_brand",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "fk_md_item_md_item_category_company_id_category_id",
                        columns: x => new { x.company_id, x.category_id },
                        principalTable: "md_item_category",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "fk_md_item_md_uom_company_id_base_uom_id",
                        columns: x => new { x.company_id, x.base_uom_id },
                        principalTable: "md_uom",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_item_company_id_base_uom_id",
                table: "md_item",
                columns: new[] { "company_id", "base_uom_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_item_company_id_brand_id",
                table: "md_item",
                columns: new[] { "company_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_item_company_id_category_id",
                table: "md_item",
                columns: new[] { "company_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_item_company_id_name",
                table: "md_item",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_item_company_id_code",
                table: "md_item",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_item");
        }
    }
}
