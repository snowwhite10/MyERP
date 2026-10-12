using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_ItemImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_item_image",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<int>(type: "int", nullable: false),
                    width = table.Column<int>(type: "int", nullable: false),
                    height = table.Column<int>(type: "int", nullable: false),
                    content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_item_image", x => x.id);
                    table.CheckConstraint("ck_md_item_image_content_type", "[content_type] IN ('image/png', 'image/jpeg')");
                    table.CheckConstraint("ck_md_item_image_sort_order", "[sort_order] BETWEEN 0 AND 9999");
                    table.ForeignKey(
                        name: "fk_md_item_image_md_item_company_id_item_id",
                        columns: x => new { x.company_id, x.item_id },
                        principalTable: "md_item",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_item_image_company_id_item_id_sort_order",
                table: "md_item_image",
                columns: new[] { "company_id", "item_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_md_item_image_company_id_item_id_primary",
                table: "md_item_image",
                columns: new[] { "company_id", "item_id" },
                unique: true,
                filter: "[is_primary] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_item_image");
        }
    }
}
