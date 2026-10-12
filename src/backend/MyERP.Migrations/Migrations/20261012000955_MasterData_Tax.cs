using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF Core generated index column arrays

#nullable disable

namespace MyERP.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Tax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "md_tax_code",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tax_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
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
                    table.PrimaryKey("pk_md_tax_code", x => x.id);
                    table.UniqueConstraint("ak_md_tax_code_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_md_tax_code_custom_fields_json", "[custom_fields] IS NULL OR ISJSON([custom_fields]) = 1");
                    table.CheckConstraint("ck_md_tax_code_tax_type", "[tax_type] IN ('Output', 'Input', 'Both')");
                });

            migrationBuilder.CreateTable(
                name: "md_tax_rate",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tax_code_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    rate_percent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_md_tax_rate", x => x.id);
                    table.CheckConstraint("ck_md_tax_rate_period", "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
                    table.CheckConstraint("ck_md_tax_rate_rate_percent", "[rate_percent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_md_tax_rate_md_tax_code_company_id_tax_code_id",
                        columns: x => new { x.company_id, x.tax_code_id },
                        principalTable: "md_tax_code",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_md_tax_code_company_id_name",
                table: "md_tax_code",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_md_tax_code_company_id_code",
                table: "md_tax_code",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_tax_rate_company_id_tax_code_id",
                table: "md_tax_rate",
                columns: new[] { "company_id", "tax_code_id" });

            migrationBuilder.CreateIndex(
                name: "ux_md_tax_rate_company_id_tax_code_id_effective_from",
                table: "md_tax_rate",
                columns: new[] { "company_id", "tax_code_id", "effective_from" },
                unique: true,
                filter: "[is_deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "md_tax_rate");

            migrationBuilder.DropTable(
                name: "md_tax_code");
        }
    }
}
