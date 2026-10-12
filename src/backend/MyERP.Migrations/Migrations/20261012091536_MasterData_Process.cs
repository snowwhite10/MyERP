using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Process : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_process_category",
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
                    table.PrimaryKey("pk_md_process_category", x => x.id);
                    table.UniqueConstraint("ak_md_process_category_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_md_process_category_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_process_category_sort_order", "[sort_order] BETWEEN 0 AND 9999");
                });

            migrationBuilder.CreateTable(
                name: "md_process",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    uom_id = table.Column<long>(type: "bigint", nullable: true),
                    standard_rate = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
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
                    table.PrimaryKey("pk_md_process", x => x.id);
                    table.CheckConstraint("ck_md_process_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_process_sort_order", "[sort_order] BETWEEN 0 AND 9999");
                    table.CheckConstraint("ck_md_process_standard_rate", "[standard_rate] >= 0");
                    table.ForeignKey(
                        name: "fk_md_process_md_process_category_company_id_category_id",
                        columns: x => new { x.company_id, x.category_id },
                        principalTable: "md_process_category",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "fk_md_process_md_uom_company_id_uom_id",
                        columns: x => new { x.company_id, x.uom_id },
                        principalTable: "md_uom",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_process_company_id_category_id",
                table: "md_process",
                columns: new[] { "company_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_md_process_company_id_name",
                table: "md_process",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_md_process_company_id_uom_id",
                table: "md_process",
                columns: new[] { "company_id", "uom_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_process_company_id_code",
                table: "md_process",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_process_category_company_id_name",
                table: "md_process_category",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_process_category_company_id_code",
                table: "md_process_category",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_process");

            migrationBuilder.DropTable(
                name: "md_process_category");
        }
    }
}
