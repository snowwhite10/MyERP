using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration generator emits literal index column arrays.

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class System_CompanyImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sys_company_image",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    image_kind = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    content_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<int>(type: "int", nullable: false),
                    width = table.Column<int>(type: "int", nullable: false),
                    height = table.Column<int>(type: "int", nullable: false),
                    content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sys_company_image", x => x.id);
                    table.CheckConstraint("ck_sys_company_image_content_type", "[content_type] IN ('image/png', 'image/jpeg')");
                    table.CheckConstraint("ck_sys_company_image_dimensions", "[width] > 0 AND [height] > 0");
                    table.CheckConstraint("ck_sys_company_image_kind", "[image_kind] IN ('Logo', 'Stamp')");
                    table.CheckConstraint("ck_sys_company_image_size", "[size_bytes] > 0 AND DATALENGTH([content]) = [size_bytes]");
                    table.ForeignKey(
                        name: "fk_sys_company_image_sys_company_company_id",
                        column: x => x.company_id,
                        principalTable: "sys_company",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_sys_company_image_company_id_image_kind",
                table: "sys_company_image",
                columns: new[] { "company_id", "image_kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sys_company_image");
        }
    }
}
