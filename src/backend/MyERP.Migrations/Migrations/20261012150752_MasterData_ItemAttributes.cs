using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_ItemAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_item_attribute",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    custom_fields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_item_attribute", x => x.id);
                    table.UniqueConstraint("ak_md_item_attribute_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_md_item_attribute_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_item_attribute_sort_order", "[sort_order] BETWEEN 0 AND 9999");
                });

            migrationBuilder.CreateTable(
                name: "md_item_attribute_value",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_item_attribute_value", x => x.id);
                    table.CheckConstraint("ck_md_item_attribute_value_value", "LEN([value]) > 0");
                    table.ForeignKey(
                        name: "fk_md_item_attribute_value_md_item_attribute_company_id_attribute_id",
                        columns: x => new { x.company_id, x.attribute_id },
                        principalTable: "md_item_attribute",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "fk_md_item_attribute_value_md_item_company_id_item_id",
                        columns: x => new { x.company_id, x.item_id },
                        principalTable: "md_item",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_item_attribute_company_id_name",
                table: "md_item_attribute",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_item_attribute_company_id_code",
                table: "md_item_attribute",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_item_attribute_value_company_id_attribute_id",
                table: "md_item_attribute_value",
                columns: new[] { "company_id", "attribute_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_item_attribute_value_company_id_item_id_attribute_id",
                table: "md_item_attribute_value",
                columns: new[] { "company_id", "item_id", "attribute_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_item_attribute_value");

            migrationBuilder.DropTable(
                name: "md_item_attribute");
        }
    }
}
