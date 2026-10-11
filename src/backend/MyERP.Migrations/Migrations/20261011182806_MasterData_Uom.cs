using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Uom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_uom",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    decimal_places = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("pk_md_uom", x => x.id);
                    table.UniqueConstraint("ak_md_uom_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_md_uom_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_uom_decimal_places", "[decimal_places] BETWEEN 0 AND 6");
                });

            migrationBuilder.CreateTable(
                name: "md_uom_conversion",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    from_uom_id = table.Column<long>(type: "bigint", nullable: false),
                    to_uom_id = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_md_uom_conversion", x => x.id);
                    table.CheckConstraint("ck_md_uom_conversion_factor", "[factor] > 0");
                    table.CheckConstraint("ck_md_uom_conversion_units", "[from_uom_id] <> [to_uom_id]");
                    table.ForeignKey(
                        name: "fk_md_uom_conversion_md_uom_company_id_from_uom_id",
                        columns: x => new { x.company_id, x.from_uom_id },
                        principalTable: "md_uom",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "fk_md_uom_conversion_md_uom_company_id_to_uom_id",
                        columns: x => new { x.company_id, x.to_uom_id },
                        principalTable: "md_uom",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_uom_company_id_name",
                table: "md_uom",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_uom_company_id_code",
                table: "md_uom",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_uom_conversion_company_id_to_uom_id",
                table: "md_uom_conversion",
                columns: new[] { "company_id", "to_uom_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_uom_conversion_company_id_from_uom_id_to_uom_id",
                table: "md_uom_conversion",
                columns: new[] { "company_id", "from_uom_id", "to_uom_id" },
                unique: true,
                filter: "[is_deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_uom_conversion");

            migrationBuilder.DropTable(
                name: "md_uom");
        }
    }
}
